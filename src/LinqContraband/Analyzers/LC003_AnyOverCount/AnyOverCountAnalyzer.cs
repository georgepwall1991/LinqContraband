using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

/// <summary>
/// Analyzes existence checks using Count() instead of Any() on IQueryable collections. Diagnostic ID: LC003
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Using Count() > 0 on an IQueryable forces the database to count all matching records,
/// even when you only need to know if at least one exists. The Any() method is optimized to return as soon as the first
/// matching record is found, translating to SQL that uses EXISTS or TOP(1), which is significantly faster. This becomes
/// especially critical with large result sets where Count() might scan millions of rows unnecessarily.</para>
/// </remarks>
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
        // 1. Count() > 0  OR  0 < Count()
        // 2. Count() >= 1 OR  1 <= Count()
        // 3. Count() != 0 OR  0 != Count()

        if (binaryOp.OperatorKind != BinaryOperatorKind.GreaterThan &&
            binaryOp.OperatorKind != BinaryOperatorKind.LessThan &&
            binaryOp.OperatorKind != BinaryOperatorKind.GreaterThanOrEqual &&
            binaryOp.OperatorKind != BinaryOperatorKind.LessThanOrEqual &&
            binaryOp.OperatorKind != BinaryOperatorKind.NotEquals)
            return;

        IOperation? countInvocation = null;
        object? constantValue = null;

        // Determine which side is the invocation and which is the constant
        if (IsInvocation(binaryOp.LeftOperand) && IsConstant(binaryOp.RightOperand))
        {
            countInvocation = binaryOp.LeftOperand;
            constantValue = binaryOp.RightOperand.ConstantValue.Value;
        }
        else if (IsConstant(binaryOp.LeftOperand) && IsInvocation(binaryOp.RightOperand))
        {
            constantValue = binaryOp.LeftOperand.ConstantValue.Value;
            countInvocation = binaryOp.RightOperand;
        }
        else
        {
            return;
        }

        // Validate the logic
        bool isMatch = false;

        if (IsZero(constantValue))
        {
            // Count() > 0, 0 < Count(), Count() != 0, 0 != Count()
            if (binaryOp.OperatorKind == BinaryOperatorKind.GreaterThan || // Count > 0
                binaryOp.OperatorKind == BinaryOperatorKind.LessThan ||    // 0 < Count
                binaryOp.OperatorKind == BinaryOperatorKind.NotEquals)     // Count != 0
            {
                // Ensure strict direction for inequalities
                if (binaryOp.OperatorKind == BinaryOperatorKind.GreaterThan && binaryOp.LeftOperand != countInvocation) return; // 0 > Count (False)
                if (binaryOp.OperatorKind == BinaryOperatorKind.LessThan && binaryOp.RightOperand != countInvocation) return;   // Count < 0 (False)

                isMatch = true;
            }
        }
        else if (IsOne(constantValue))
        {
            // Count() >= 1, 1 <= Count()
            if (binaryOp.OperatorKind == BinaryOperatorKind.GreaterThanOrEqual ||
                binaryOp.OperatorKind == BinaryOperatorKind.LessThanOrEqual)
            {
                // Ensure direction
                // Count >= 1
                if (binaryOp.OperatorKind == BinaryOperatorKind.GreaterThanOrEqual && binaryOp.LeftOperand == countInvocation) isMatch = true;

                // 1 <= Count
                if (binaryOp.OperatorKind == BinaryOperatorKind.LessThanOrEqual && binaryOp.RightOperand == countInvocation) isMatch = true;
            }
        }

        if (!isMatch) return;

        // Unwrap implicit conversions or await operations
        while (true)
        {
            if (countInvocation is IConversionOperation conversion)
            {
                countInvocation = conversion.Operand;
            }
            else if (countInvocation is IAwaitOperation awaitOp)
            {
                countInvocation = awaitOp.Operation;
            }
            else
            {
                break;
            }
        }

        if (countInvocation is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;

            var isSyncCount = (method.Name == "Count" || method.Name == "LongCount") &&
                              method.ContainingType.Name == "Queryable" &&
                              method.ContainingNamespace?.ToString() == "System.Linq";

            var isAsyncCount = (method.Name == "CountAsync" || method.Name == "LongCountAsync") &&
                               method.ContainingType.Name == "EntityFrameworkQueryableExtensions" &&
                               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";

            if (isSyncCount || isAsyncCount)
            {
                // Check if the source is IQueryable
                var receiverType = invocation.GetInvocationReceiverType();

                if (receiverType?.IsIQueryable() == true)
                    context.ReportDiagnostic(Diagnostic.Create(Rule, binaryOp.Syntax.GetLocation()));
            }
        }
    }

    private bool IsInvocation(IOperation op)
    {
        var unwrapped = op.UnwrapConversions();
        return unwrapped is IInvocationOperation;
    }

    private bool IsConstant(IOperation op)
    {
        return op.ConstantValue.HasValue;
    }

    private bool IsZero(object? value)
    {
        if (value is int i) return i == 0;
        if (value is long l) return l == 0;
        return false;
    }

    private bool IsOne(object? value)
    {
        if (value is int i) return i == 1;
        if (value is long l) return l == 1;
        return false;
    }
}
