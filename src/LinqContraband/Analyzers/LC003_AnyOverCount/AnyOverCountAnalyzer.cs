using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AnyOverCountAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC003";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Prefer Any() over Count() > 0";

    private static readonly LocalizableString MessageFormat =
        "Use Any() instead of Count() > 0 for efficient existence checking on IQueryable";

    private static readonly LocalizableString Description =
        "Checking if Count() is greater than 0 on an IQueryable can be expensive as it may iterate the entire result set. Any() is optimized to return as soon as a match is found.";

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
        context.RegisterOperationAction(AnalyzeBinaryOperator, OperationKind.Binary);
    }

    private void AnalyzeBinaryOperator(OperationAnalysisContext context)
    {
        var binaryOp = (IBinaryOperation)context.Operation;

        // We are looking for:
        // 1. Count() > 0
        // 2. 0 < Count()

        if (binaryOp.OperatorKind != BinaryOperatorKind.GreaterThan &&
            binaryOp.OperatorKind != BinaryOperatorKind.LessThan)
            return;

        IOperation? countInvocation = null;
        object? constantValue = null;

        if (binaryOp.OperatorKind == BinaryOperatorKind.GreaterThan)
        {
            // Count() > 0
            countInvocation = binaryOp.LeftOperand;
            constantValue = binaryOp.RightOperand.ConstantValue.HasValue
                ? binaryOp.RightOperand.ConstantValue.Value
                : null;
        }
        else // LessThan
        {
            // 0 < Count()
            constantValue = binaryOp.LeftOperand.ConstantValue.HasValue
                ? binaryOp.LeftOperand.ConstantValue.Value
                : null;
            countInvocation = binaryOp.RightOperand;
        }

        // Check if constant is 0 (int or long)
        if (!IsZero(constantValue)) return;

        // Unwrap implicit conversions if any
        while (countInvocation is IConversionOperation conversion) countInvocation = conversion.Operand;

        if (countInvocation is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if ((method.Name == "Count" || method.Name == "LongCount") &&
                method.ContainingType.Name == "Queryable" && // Explicitly for IQueryable extension methods
                method.ContainingNamespace?.ToString() == "System.Linq")
            {
                // Check if the source is IQueryable
                var receiverType = invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null;

                if (receiverType.IsIQueryable())
                    context.ReportDiagnostic(Diagnostic.Create(Rule, binaryOp.Syntax.GetLocation()));
            }
        }
    }

    private bool IsZero(object? value)
    {
        if (value is int i) return i == 0;
        if (value is long l) return l == 0;
        return false;
    }
}