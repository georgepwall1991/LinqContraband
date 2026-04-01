using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static IOperation? GetInvocationReceiver(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        var receiver = invocation.Instance ??
                       (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        if (unwrapConversions && receiver != null)
            receiver = receiver.UnwrapConversions();

        return receiver;
    }

    public static ITypeSymbol? GetInvocationReceiverType(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        return invocation.GetInvocationReceiver(unwrapConversions)?.Type;
    }

    public static IOperation UnwrapConversions(this IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            if (current is IAwaitOperation awaitOp)
            {
                current = awaitOp.Operation;
                continue;
            }

            break;
        }

        return current ?? operation;
    }
}
