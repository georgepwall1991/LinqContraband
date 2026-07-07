using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

public sealed partial class AvoidExecuteSqlRawWithInterpolationAnalyzer
{
    private static bool IsPotentiallyUnsafeSql(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation interpolatedString)
            return HasNonConstantInterpolation(interpolatedString);

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
            return IsUnsafeConcatenation(binary);

        return false;
    }

    private static bool IsUnsafeConcatenation(IBinaryOperation binary)
    {
        return IsUnsafeSide(binary.LeftOperand) || IsUnsafeSide(binary.RightOperand);
    }

    private static bool IsUnsafeSide(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        if (current is IBinaryOperation nestedBinary && nestedBinary.OperatorKind == BinaryOperatorKind.Add)
            return IsUnsafeConcatenation(nestedBinary);

        return true;
    }

    private static bool HasNonConstantInterpolation(IInterpolatedStringOperation interpolatedString)
    {
        return interpolatedString.Parts
            .OfType<IInterpolationOperation>()
            .Any(interpolation => !interpolation.Expression.UnwrapConversions().ConstantValue.HasValue);
    }
}
