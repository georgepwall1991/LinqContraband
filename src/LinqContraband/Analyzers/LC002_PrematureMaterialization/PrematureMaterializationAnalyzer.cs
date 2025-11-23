using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PrematureMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC002";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Premature materialization of IQueryable";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' on materialized collection but source was IQueryable. This fetches all data before filtering.";

    private static readonly LocalizableString Description =
        "Ensure filtering happens before materialization (ToList, ToArray, etc).";

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

        var receiverType = invocation.Instance?.Type ??
                           (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);

        if (receiverType == null) return;

        if (receiverType.IsIQueryable()) return;

        if (!IsLinqOperator(methodSymbol)) return;

        var receiverOp = invocation.Instance ??
                         (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        while (receiverOp is IConversionOperation conversion) receiverOp = conversion.Operand;

        if (receiverOp is IInvocationOperation previousInvocation)
            if (IsMaterializingMethod(previousInvocation.TargetMethod))
            {
                // Check *that* method's receiver. Was it IQueryable?
                var sourceOp = previousInvocation.Instance ??
                               (previousInvocation.Arguments.Length > 0 ? previousInvocation.Arguments[0].Value : null);

                // Handle conversion on sourceOp too (e.g. implicit conversion from List to IEnumerable in chain)
                while (sourceOp is IConversionOperation conversion) sourceOp = conversion.Operand;

                var sourceType = sourceOp?.Type;
                if (sourceType.IsIQueryable())
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
            }
    }

    private bool IsLinqOperator(IMethodSymbol method)
    {
        return method.ContainingType.Name == "Enumerable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private bool IsMaterializingMethod(IMethodSymbol method)
    {
        return (method.Name == "ToList" || method.Name == "ToArray" || method.Name == "AsEnumerable") &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }
}