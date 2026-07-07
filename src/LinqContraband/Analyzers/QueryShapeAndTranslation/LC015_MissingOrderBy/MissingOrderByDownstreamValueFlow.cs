using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
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
}
