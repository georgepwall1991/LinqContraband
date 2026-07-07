using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool IsAsNoTrackingMaterialization(IOperation operation, out IOperation? queryReceiver)
    {
        queryReceiver = null;
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is not IInvocationOperation invocation) return false;

        if (!invocation.TargetMethod.Name.IsMaterializerMethod()) return false;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null) return false;

        if (!ChainContainsAsNoTracking(receiver, out queryReceiver)) return false;

        return true;
    }

    private static bool ChainContainsAsNoTracking(IOperation operation, out IOperation? rootReceiver)
    {
        rootReceiver = null;
        var current = operation.UnwrapConversions();

        // The last tracking directive applied wins (each AsTracking/AsNoTracking overwrites the
        // query's QueryTrackingBehavior). Walking up the receiver chain, the first directive
        // encountered is the one applied last, so it decides the effective mode: a trailing
        // AsTracking() (AsNoTracking().AsTracking()) makes the entity tracked, so the mutation
        // persists and LC044 must not fire. Keep walking to the root receiver regardless.
        bool? effectiveNoTracking = null;

        while (current is IInvocationOperation inv)
        {
            if (effectiveNoTracking is null)
            {
                if (inv.TargetMethod.Name == "AsNoTracking")
                    effectiveNoTracking = true;
                else if (inv.TargetMethod.Name == "AsTracking")
                    effectiveNoTracking = false;
            }

            var next = inv.GetInvocationReceiver();
            if (next == null)
            {
                rootReceiver = inv;
                break;
            }

            current = next.UnwrapConversions();
        }

        if (effectiveNoTracking != true) return false;

        if (rootReceiver == null) rootReceiver = current;
        return true;
    }

    private static bool TryGetQueryContextSymbol(IOperation? queryReceiver, out ISymbol? contextSymbol)
    {
        contextSymbol = null;
        if (queryReceiver == null) return false;

        var current = queryReceiver.UnwrapConversions();

        while (current != null)
        {
            switch (current)
            {
                case IPropertyReferenceOperation propRef when propRef.Type.IsDbSet():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(propRef.Instance, out contextSymbol);

                case IFieldReferenceOperation fieldRef when fieldRef.Type.IsDbSet():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(fieldRef.Instance, out contextSymbol);

                case IInvocationOperation inv when inv.TargetMethod.Name == "Set" &&
                                                   inv.TargetMethod.ContainingType.IsDbContext():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(inv.Instance, out contextSymbol);

                case IInvocationOperation inv:
                    var next = inv.GetInvocationReceiver();
                    if (next == null) return false;
                    current = next.UnwrapConversions();
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }
}
