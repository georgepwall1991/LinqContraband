using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

/// <summary>
/// Analyzes local method calls within IQueryable expressions that cannot be translated to SQL. Diagnostic ID: LC001
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Methods invoked inside an IQueryable expression must be translatable to SQL by the query provider.
/// Local methods and custom user-defined methods cannot be translated, causing the entire query to be evaluated client-side,
/// which results in fetching all data from the database into memory before filtering. This defeats the purpose of using IQueryable
/// and can cause severe performance degradation and memory issues when working with large datasets.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LocalMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC001";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Client-side evaluation risk: Local method usage in IQueryable";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' cannot be translated to SQL and may cause client-side evaluation";

    private static readonly LocalizableString Description =
        "Methods invoked inside an IQueryable expression must be translatable to SQL.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var methodSymbol = invocation.TargetMethod;

        // Constraint 3: Method defined in Source Code
        // We use IsFrameworkMethod check as IsInSource can be unreliable in some test contexts
        // and we want to exclude System/Microsoft methods explicitly.
        if (methodSymbol.MethodKind != MethodKind.Ordinary ||
            methodSymbol.IsImplicitlyDeclared ||
            methodSymbol.IsFrameworkMethod())
            return;

        // Constraint 1: Inside a Lambda
        var parent = invocation.Parent;
        IOperation? lambda = null;

        while (parent != null)
        {
            if (parent.Kind == OperationKind.AnonymousFunction)
            {
                lambda = parent;
                break;
            }

            parent = parent.Parent;
        }

        if (lambda == null) return;

        // Constraint 2: Lambda is argument to IQueryable extension method
        var current = lambda.Parent;
        while (current != null)
        {
            if (current is IInvocationOperation queryInvocation)
            {
                // Handle both extension syntax (Instance populated) and static call syntax (Instance null, use first arg)
                var type = queryInvocation.Instance?.Type;
                if (type == null && queryInvocation.Arguments.Length > 0)
                    type = queryInvocation.Arguments[0].Value.Type;

                if (type.IsIQueryable())
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
                    return;
                }
            }

            current = current.Parent;
        }
    }
}
