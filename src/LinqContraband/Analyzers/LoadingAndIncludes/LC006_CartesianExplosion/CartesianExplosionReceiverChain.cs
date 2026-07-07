using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    private static ImmutableArray<IInvocationOperation> CollectReceiverChainInvocations(IInvocationOperation outermostInvocation)
    {
        var builder = ImmutableArray.CreateBuilder<IInvocationOperation>();
        IOperation? current = outermostInvocation;

        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is IInvocationOperation invocation)
            {
                builder.Add(invocation);
                current = invocation.GetInvocationReceiver();
                continue;
            }

            // Include chains can be split across a single-assignment local. Resolve that local
            // back to its assigned value so sibling Includes and AsSplitQuery still participate.
            if (current is ILocalReferenceOperation localReference)
            {
                var executableRoot = localReference.FindOwningExecutableRoot();
                if (executableRoot != null &&
                    LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        out var assignedValue))
                {
                    current = assignedValue;
                    continue;
                }
            }

            break;
        }

        builder.Reverse();
        return builder.ToImmutable();
    }

    private static bool HasRelevantQueryOperatorAncestor(IInvocationOperation invocation)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is not IInvocationOperation parentInvocation)
                continue;

            if (!IsRelevantQueryOperator(parentInvocation.TargetMethod))
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
