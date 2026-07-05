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

    private bool HasSortingDownstreamThroughLocal(
        IInvocationOperation invocation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        if (!TryGetAssignedLocal(invocation, out var local))
            return false;

        var executableRoot = invocation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        foreach (var descendant in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant.Syntax.SpanStart <= invocation.Syntax.SpanStart ||
                descendant is not IInvocationOperation downstream ||
                !SortingMethods.Contains(downstream.TargetMethod.Name))
            {
                continue;
            }

            var receiver = downstream.GetInvocationReceiver();
            if (receiver?.UnwrapConversions() is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (TryResolveLocalValue(
                    local,
                    localReference,
                    executableRoot,
                    localValueCache,
                    cancellationToken,
                    out var resolvedValue) &&
                IsSameOperation(resolvedValue, invocation))
            {
                return !HasPaginationAfterDownstreamSort(
                    downstream,
                    localValueCache,
                    cancellationToken);
            }
        }

        return false;
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

    private bool ReceivesFromOperation(
        IOperation operation,
        IOperation target,
        IOperation executableRoot,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var current = operation;

        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            current = current.UnwrapConversions();

            if (IsSameOperation(current, target))
                return true;

            if (current is IInvocationOperation invocation)
            {
                current = invocation.GetInvocationReceiver();
                continue;
            }

            if (current is ILocalReferenceOperation localReference &&
                TryResolveLocalValue(
                    localReference.Local,
                    localReference,
                    executableRoot,
                    localValueCache,
                    cancellationToken,
                    out var resolvedValue))
            {
                current = resolvedValue;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool TryGetAssignedLocal(IInvocationOperation invocation, out ILocalSymbol local)
    {
        local = null!;

        var current = invocation.Parent;
        while (current is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        if (current is IVariableInitializerOperation initializer &&
            initializer.Parent is IVariableDeclaratorOperation declarator)
        {
            local = declarator.Symbol;
            return true;
        }

        if (current is ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference)
        {
            local = localReference.Local;
            return true;
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
