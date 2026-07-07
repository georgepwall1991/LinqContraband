using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private static bool HasEntityFrameworkQuerySource(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var visitedLocalReferences = new HashSet<LocalReferenceKey>(LocalReferenceKeyComparer.Instance);
        var current = operation;
        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.UnwrapConversions();

            if (current.Type.IsDbSet())
                return true;

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (invocation.TargetMethod.Name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                        return true;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    current = propertyReference.Instance;
                    continue;

                case IFieldReferenceOperation fieldReference:
                    current = fieldReference.Instance;
                    continue;

                case ILocalReferenceOperation localReference:
                    if (!visitedLocalReferences.Add(new LocalReferenceKey(localReference.Local, localReference.Syntax.SpanStart)))
                        return false;

                    if (localReference.Type.IsDbSet())
                        return true;

                    if (TryResolveLocalValue(
                            localReference.Local,
                            localReference,
                            localReference.FindOwningExecutableRoot(),
                            localValueCache,
                            cancellationToken,
                            out var resolvedValue))
                    {
                        current = resolvedValue;
                        continue;
                    }

                    return false;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryResolveLocalValue(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken,
        out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        return localValueCache.TryGetLatestValue(
            executableRoot,
            local,
            reference.Syntax.SpanStart,
            cancellationToken,
            out value);
    }
}
