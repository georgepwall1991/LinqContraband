using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public sealed partial class FindInsteadOfFirstOrDefaultAnalyzer
{
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
