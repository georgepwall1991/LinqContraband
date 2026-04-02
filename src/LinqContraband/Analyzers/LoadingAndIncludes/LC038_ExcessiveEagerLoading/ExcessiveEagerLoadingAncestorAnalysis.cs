using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC038_ExcessiveEagerLoading;

public sealed partial class ExcessiveEagerLoadingAnalyzer
{
    private static bool HasIncludeAncestor(IInvocationOperation invocation)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is not IInvocationOperation parentInvocation)
                continue;

            if (!IsIncludeLike(parentInvocation.TargetMethod))
                continue;

            if (InvocationUsesReceiverChain(parentInvocation.GetInvocationReceiver(), invocation))
                return true;
        }

        return false;
    }

    private static bool InvocationUsesReceiverChain(IOperation? current, IInvocationOperation target)
    {
        current = current?.UnwrapConversions();

        while (current != null)
        {
            if (ReferenceEquals(current, target))
                return true;

            if (current is IInvocationOperation invocation)
            {
                current = invocation.GetInvocationReceiver();
                continue;
            }

            break;
        }

        return false;
    }
}
