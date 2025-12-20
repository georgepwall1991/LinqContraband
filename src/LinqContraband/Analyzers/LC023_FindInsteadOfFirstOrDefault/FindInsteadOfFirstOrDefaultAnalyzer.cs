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
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC023_FindInsteadOfFirstOrDefault.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "First", "FirstOrDefault", "Single", "SingleOrDefault",
        "FirstAsync", "FirstOrDefaultAsync", "SingleAsync", "SingleOrDefaultAsync"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

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
        if (IsPrimaryKeyEquality(lambda, out var pkName))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsPrimaryKeyEquality(IAnonymousFunctionOperation lambda, out string? pkName)
    {
        pkName = null;
        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp) body = returnOp.ReturnedValue;
        if (body == null) return false;

        body = body.UnwrapConversions();

        if (body is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Equals)
        {
            // Check left or right for primary key property
            if (IsPrimaryKeyProperty(binary.LeftOperand, lambda, out pkName)) return true;
            if (IsPrimaryKeyProperty(binary.RightOperand, lambda, out pkName)) return true;
        }

        return false;
    }

    private bool IsPrimaryKeyProperty(IOperation operation, IAnonymousFunctionOperation lambda, out string? pkName)
    {
        pkName = null;
        var current = operation.UnwrapConversions();

        if (current is IPropertyReferenceOperation propRef)
        {
            var receiver = propRef.Instance?.UnwrapConversions();
            // Check if receiver is the lambda parameter
            if (receiver is IParameterReferenceOperation paramRef && 
                SymbolEqualityComparer.Default.Equals(paramRef.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
            {
                var pk = propRef.Property.ContainingType.TryFindPrimaryKey();
                if (pk != null && propRef.Property.Name == pk)
                {
                    pkName = pk;
                    return true;
                }
            }
        }

        return false;
    }
}
