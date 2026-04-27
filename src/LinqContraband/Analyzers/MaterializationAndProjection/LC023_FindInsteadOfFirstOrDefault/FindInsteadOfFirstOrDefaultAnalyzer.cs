using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Analyzes usage of FirstOrDefault/SingleOrDefault with primary key predicates, suggesting Find/FindAsync instead for better performance via change tracker. Diagnostic ID: LC023
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FindInsteadOfFirstOrDefaultAnalyzer : DiagnosticAnalyzer
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
            if (primaryKey != null && property.Name == primaryKey)
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private static bool TryGetPrimaryKeyEqualityProperty(
        IAnonymousFunctionOperation lambda,
        out IPropertySymbol property)
    {
        property = null!;
        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp) body = returnOp.ReturnedValue;
        if (body == null) return false;

        body = body.UnwrapConversions();

        if (body is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Equals)
        {
            // Check left or right for primary key property
            if (TryGetLambdaParameterProperty(binary.LeftOperand, lambda, out property)) return true;
            if (TryGetLambdaParameterProperty(binary.RightOperand, lambda, out property)) return true;
        }

        return false;
    }

    private static bool TryGetLambdaParameterProperty(
        IOperation operation,
        IAnonymousFunctionOperation lambda,
        out IPropertySymbol property)
    {
        property = null!;
        var current = operation.UnwrapConversions();

        if (current is IPropertyReferenceOperation propRef)
        {
            var receiver = propRef.Instance?.UnwrapConversions();
            // Check if receiver is the lambda parameter
            if (receiver is IParameterReferenceOperation paramRef &&
                SymbolEqualityComparer.Default.Equals(paramRef.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
            {
                property = propRef.Property;
                return true;
            }
        }

        return false;
    }
}
