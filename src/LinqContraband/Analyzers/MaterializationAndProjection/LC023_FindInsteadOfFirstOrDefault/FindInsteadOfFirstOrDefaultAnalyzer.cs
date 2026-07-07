using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Analyzes usage of FirstOrDefault/SingleOrDefault with primary key predicates, suggesting Find/FindAsync instead for better performance via change tracker. Diagnostic ID: LC023
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class FindInsteadOfFirstOrDefaultAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC023";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Use Find/FindAsync for primary key lookups";

    private static readonly LocalizableString MessageFormat =
        "Use 'Find' or 'FindAsync' instead of '{0}' when querying by primary key to leverage the change tracker cache";

    private static readonly LocalizableString Description =
        "Find() first checks the local change tracker before hitting the database, which can be significantly faster if the entity is already loaded.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC023_FindInsteadOfFirstOrDefault.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "FirstOrDefault", "SingleOrDefault",
        "FirstOrDefaultAsync", "SingleOrDefaultAsync"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var primaryKeyCache = FindInsteadOfFirstOrDefaultKeyAnalysis.CreateAnalyzerCache(context.Compilation);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, primaryKeyCache),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        FindInsteadOfFirstOrDefaultKeyAnalysis.PrimaryKeyCache primaryKeyCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name == "HasKey")
            primaryKeyCache.RegisterConfiguredPrimaryKey(invocation);

        if (method.Name == "HasQueryFilter")
            primaryKeyCache.RegisterQueryFilter(invocation);

        if (!TargetMethods.Contains(method.Name)) return;

        // Ensure receiver is directly a DbSet (Find doesn't work on complex queries)
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null || !receiver.Type.IsDbSet()) return;

        // Check if there is a predicate
        if (invocation.Arguments.Length < (method.IsExtensionMethod ? 2 : 1)) return;

        var predicateArg = method.IsExtensionMethod ? invocation.Arguments[1] : invocation.Arguments[0];
        var lambda = predicateArg.Value.UnwrapConversions() as IAnonymousFunctionOperation;
        if (lambda == null) return;

        // Analyze predicate body for x.Id == id
        if (TryGetPrimaryKeyEqualityProperty(lambda, out var property))
        {
            if (context.Operation.SemanticModel != null)
            {
                primaryKeyCache.EnsureSyntaxTreeScanned(
                    invocation.Syntax.SyntaxTree,
                    context.Operation.SemanticModel,
                    context.CancellationToken);
            }

            var primaryKey = primaryKeyCache.TryFindSafePrimaryKey(
                property.ContainingType,
                context.CancellationToken);
            if (primaryKey == null || property.Name != primaryKey)
                return;

            // Find returns an already-tracked instance even when a global query filter
            // would exclude it (only its database fallback applies filters), so on a
            // HasQueryFilter entity the rewrite can resurrect soft-deleted/other-tenant
            // rows. The filtered FirstOrDefault is semantically meaningful — stay quiet.
            // Check the DbSet's entity type, not the key's declaring type: an inherited
            // Id puts property.ContainingType at the base class while the filter is
            // registered for the concrete entity.
            var dbSetEntityType = receiver.Type is INamedTypeSymbol namedDbSet && namedDbSet.TypeArguments.Length == 1
                ? namedDbSet.TypeArguments[0]
                : property.ContainingType;
            if (primaryKeyCache.HasQueryFilter(dbSetEntityType, context.CancellationToken))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }
}
