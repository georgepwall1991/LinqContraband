using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC019_ConditionalInclude;

/// <summary>
/// Detects conditional expressions (ternary or null-coalesce) inside Include/ThenInclude lambdas,
/// which always throw InvalidOperationException at runtime. Diagnostic ID: LC019
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConditionalIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC019";
    private const string Category = "Correctness";
    private static readonly LocalizableString Title = "Conditional Include Expression";

    private static readonly LocalizableString MessageFormat =
        "Conditional expressions in Include/ThenInclude are not supported by EF Core and will throw at runtime";

    private static readonly LocalizableString Description =
        "EF Core cannot translate conditional (ternary or null-coalescing) expressions inside Include/ThenInclude. Split into separate conditional Include calls instead.";

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
        var method = invocation.TargetMethod;

        // Match Include/ThenInclude from EF Core namespace
        if (method.Name is not ("Include" or "ThenInclude")) return;
        if (method.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore") return;

        // Find the lambda argument
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value;
            // Unwrap conversions
            while (value is IConversionOperation conv)
                value = conv.Operand;
            while (value is IDelegateCreationOperation del)
                value = del.Target;

            if (value is not IAnonymousFunctionOperation lambda) continue;

            // Check the lambda body for conditional/coalesce operations
            var body = lambda.Body;
            if (body is IBlockOperation block)
            {
                // For expression lambdas wrapped in a block with a return
                foreach (var op in block.Operations)
                {
                    if (op is IReturnOperation ret && ret.ReturnedValue != null)
                    {
                        if (IsConditionalExpression(ret.ReturnedValue))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
                            return;
                        }
                    }
                }
            }
        }
    }

    private static bool IsConditionalExpression(IOperation operation)
    {
        var current = operation;
        while (current is IConversionOperation conv)
            current = conv.Operand;

        return current is IConditionalOperation or ICoalesceOperation;
    }
}
