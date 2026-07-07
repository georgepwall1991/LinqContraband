using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal sealed partial class AsNoTrackingThenModifyRootScan
{
    internal static bool TryGetSymbol(IOperation? operation, out ISymbol? symbol)
    {
        symbol = null;
        if (operation == null) return false;

        switch (operation.UnwrapConversions())
        {
            case ILocalReferenceOperation localRef:
                symbol = localRef.Local;
                return true;
            case IParameterReferenceOperation paramRef:
                symbol = paramRef.Parameter;
                return true;
            case IFieldReferenceOperation fieldRef:
                symbol = fieldRef.Field;
                return true;
            case IPropertyReferenceOperation propRef:
                symbol = propRef.Property;
                return true;
            case IInstanceReferenceOperation:
                symbol = null;
                return false;
            default:
                return false;
        }
    }

    private static void AddMutation(AsNoTrackingThenModifyRootScan scan, ILocalSymbol local, MutationEntry entry)
    {
        AddToBucket(scan.MutationsByLocal, local, entry);
    }

    private static void AddReattach(AsNoTrackingThenModifyRootScan scan, ILocalSymbol local, ReattachEntry entry)
    {
        AddToBucket(scan.ReattachesByLocal, local, entry);
    }

    private static void AddDetach(AsNoTrackingThenModifyRootScan scan, ILocalSymbol local, DetachEntry entry)
    {
        AddToBucket(scan.DetachesByLocal, local, entry);
    }

    private static void AddToBucket<TEntry>(
        Dictionary<ILocalSymbol, List<TEntry>> buckets,
        ILocalSymbol local,
        TEntry entry)
    {
        if (!buckets.TryGetValue(local, out var list))
        {
            list = new List<TEntry>();
            buckets[local] = list;
        }

        list.Add(entry);
    }
}
