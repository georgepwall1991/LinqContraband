using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public sealed partial class AvoidFromSqlRawWithInterpolationAnalyzer
{
    private static bool IsPotentiallyUnsafe(IOperation operation)
    {
        var current = operation;

        // Handle conversion to RawSqlString or other string-like types.
        if (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }

        current = current.UnwrapConversions();

        if (current is IInterpolatedStringOperation interpolatedString)
        {
            return HasNonConstantInterpolation(interpolatedString);
        }

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
        {
            return IsUnsafeConcatenation(binary);
        }

        return false;
    }

    private static bool IsUnsafeConcatenation(IBinaryOperation binary)
    {
        if (IsUnsafeSide(binary.LeftOperand)) return true;
        if (IsUnsafeSide(binary.RightOperand)) return true;
        return false;
    }

    private static bool IsUnsafeSide(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue) return false;

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
        {
            return IsUnsafeConcatenation(binary);
        }

        return true;
    }

    private static bool HasNonConstantInterpolation(IInterpolatedStringOperation interpolatedString)
    {
        return interpolatedString.Parts
            .OfType<IInterpolationOperation>()
            .Any(interpolation => !interpolation.Expression.UnwrapConversions().ConstantValue.HasValue);
    }
}
