using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private bool HasSortingDownstream(IInvocationOperation invocation)
    {
        IOperation current = invocation;

        while (TryGetDownstreamInvocation(current) is { } downstream)
        {
            if (SortingMethods.Contains(downstream.TargetMethod.Name))
                return true;

            current = downstream;
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
