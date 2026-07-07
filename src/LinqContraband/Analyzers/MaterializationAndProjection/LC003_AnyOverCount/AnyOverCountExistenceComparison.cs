using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

public sealed partial class AnyOverCountAnalyzer
{
    private static bool TryGetCountExistenceCheck(IBinaryOperation binaryOp, out IOperation countInvocation)
    {
        countInvocation = null!;

        if (binaryOp.OperatorKind != BinaryOperatorKind.GreaterThan &&
            binaryOp.OperatorKind != BinaryOperatorKind.LessThan &&
            binaryOp.OperatorKind != BinaryOperatorKind.GreaterThanOrEqual &&
            binaryOp.OperatorKind != BinaryOperatorKind.LessThanOrEqual &&
            binaryOp.OperatorKind != BinaryOperatorKind.NotEquals &&
            binaryOp.OperatorKind != BinaryOperatorKind.Equals)
        {
            return false;
        }

        object? constantValue;

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
            return false;
        }

        if (IsZero(constantValue))
        {
            return binaryOp.OperatorKind switch
            {
                BinaryOperatorKind.GreaterThan => binaryOp.LeftOperand == countInvocation,
                BinaryOperatorKind.LessThan => binaryOp.RightOperand == countInvocation,
                BinaryOperatorKind.NotEquals or BinaryOperatorKind.Equals => true,
                _ => false
            };
        }

        if (IsOne(constantValue))
        {
            return binaryOp.OperatorKind switch
            {
                BinaryOperatorKind.GreaterThanOrEqual => binaryOp.LeftOperand == countInvocation,
                BinaryOperatorKind.LessThanOrEqual => binaryOp.RightOperand == countInvocation,
                _ => false
            };
        }

        return false;
    }

    private static bool IsInvocation(IOperation op)
    {
        var unwrapped = op.UnwrapConversions();
        return unwrapped is IInvocationOperation;
    }

    private static bool IsConstant(IOperation op)
    {
        return op.ConstantValue.HasValue;
    }

    private static bool IsZero(object? value)
    {
        if (value is int i) return i == 0;
        if (value is long l) return l == 0;
        return false;
    }

    private static bool IsOne(object? value)
    {
        if (value is int i) return i == 1;
        if (value is long l) return l == 1;
        return false;
    }
}
