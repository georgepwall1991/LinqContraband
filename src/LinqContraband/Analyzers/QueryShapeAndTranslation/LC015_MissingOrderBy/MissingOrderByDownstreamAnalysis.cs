using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private bool HasSortingDownstream(
        IInvocationOperation invocation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        IOperation current = invocation;

        while (TryGetDownstreamInvocation(current) is { } downstream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SortingMethods.Contains(downstream.TargetMethod.Name))
            {
                return !HasPaginationAfterDownstreamSort(
                    downstream,
                    localValueCache,
                    cancellationToken);
            }

            current = downstream;
        }

        return HasSortingDownstreamThroughLocal(invocation, localValueCache, cancellationToken);
    }

    private bool HasPaginationAfterDownstreamSort(
        IInvocationOperation sortingInvocation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        IOperation current = sortingInvocation;

        while (TryGetDownstreamInvocation(current) is { } downstream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (PaginationMethods.Contains(downstream.TargetMethod.Name))
                return true;

            current = downstream;
        }

        var executableRoot = sortingInvocation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        foreach (var descendant in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant.Syntax.SpanStart <= sortingInvocation.Syntax.SpanStart ||
                descendant is not IInvocationOperation downstream ||
                !PaginationMethods.Contains(downstream.TargetMethod.Name))
            {
                continue;
            }

            var receiver = downstream.GetInvocationReceiver();
            if (receiver != null &&
                ReceivesFromOperation(
                    receiver,
                    sortingInvocation,
                    executableRoot,
                    localValueCache,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private IInvocationOperation? TryGetDownstreamInvocation(IOperation operation)
    {
        var unwrappedOperation = operation.UnwrapConversions();
        var current = operation;

        while (current.Parent != null)
        {
            current = current.Parent;

            if (current is IConversionOperation or IParenthesizedOperation or IAwaitOperation or IArgumentOperation)
                continue;

            if (current is not IInvocationOperation invocation)
                return null;

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null)
                return null;

            return IsSameOperation(receiver, unwrappedOperation) ? invocation : null;
        }

        return null;
    }

    private bool IsSameOperation(IOperation left, IOperation right)
    {
        var unwrappedLeft = left.UnwrapConversions();
        var unwrappedRight = right.UnwrapConversions();

        return ReferenceEquals(unwrappedLeft, unwrappedRight) ||
               unwrappedLeft.Syntax == unwrappedRight.Syntax;
    }
}
