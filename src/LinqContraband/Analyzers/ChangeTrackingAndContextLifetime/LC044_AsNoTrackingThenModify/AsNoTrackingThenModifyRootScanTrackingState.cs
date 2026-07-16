using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal sealed partial class AsNoTrackingThenModifyRootScan
{
    private static readonly ImmutableHashSet<string> UpdateMethodNames = ImmutableHashSet.Create(
        "Update", "UpdateRange");

    private static readonly ImmutableHashSet<string> AttachMethodNames = ImmutableHashSet.Create(
        "Attach", "AttachRange");

    private static readonly ImmutableHashSet<string> TrackingStates = ImmutableHashSet.Create(
        "Modified", "Added");

    private static bool TryParseReattachInvocation(
        IInvocationOperation invocation,
        out ISymbol? contextSymbol,
        out bool persistsExistingMutation)
    {
        contextSymbol = null;
        persistsExistingMutation = false;

        var methodName = invocation.TargetMethod.Name;
        if (!UpdateMethodNames.Contains(methodName) && !AttachMethodNames.Contains(methodName))
            return false;

        var container = invocation.TargetMethod.ContainingType;
        if (!container.IsDbContext() && !container.IsDbSet()) return false;

        if (!TryResolveInvocationContext(invocation, out contextSymbol)) return false;

        persistsExistingMutation = UpdateMethodNames.Contains(methodName);
        return invocation.Arguments.Length > 0;
    }

    private static bool TryParseEntryStateAssignment(
        ISimpleAssignmentOperation assignment,
        out ILocalSymbol local,
        out ISymbol? contextSymbol,
        out ImmutableArray<MemberPathSegment> targetPath,
        out string stateName)
    {
        local = null!;
        contextSymbol = null;
        targetPath = ImmutableArray<MemberPathSegment>.Empty;
        stateName = "";

        if (assignment.Target is not IPropertyReferenceOperation targetProp) return false;
        if (targetProp.Property.Name != "State") return false;
        if (targetProp.Property.ContainingType?.Name != "EntityEntry") return false;

        if (targetProp.Instance is not IInvocationOperation entryInv) return false;
        if (entryInv.TargetMethod.Name != "Entry") return false;
        if (!entryInv.TargetMethod.ContainingType.IsDbContext()) return false;

        if (entryInv.Arguments.Length == 0) return false;
        if (!TryGetRootLocalAndMemberPath(entryInv.Arguments[0].Value, out local, out targetPath))
            return false;

        if (!TryGetSymbol(entryInv.Instance, out contextSymbol)) return false;

        var value = assignment.Value.UnwrapConversions();
        if (value is not IFieldReferenceOperation fieldRef) return false;
        if (fieldRef.Field.ContainingType?.Name != "EntityState") return false;
        if (!TrackingStates.Contains(fieldRef.Field.Name) && fieldRef.Field.Name != "Detached") return false;

        stateName = fieldRef.Field.Name;
        return true;
    }

    private static bool TryParseTrackerClear(IInvocationOperation invocation, out ISymbol? contextSymbol)
    {
        contextSymbol = null;

        if (invocation.TargetMethod.Name != "Clear") return false;
        if (invocation.TargetMethod.ContainingType?.Name != "ChangeTracker") return false;

        if (invocation.Instance?.UnwrapConversions() is not IPropertyReferenceOperation propRef) return false;
        if (propRef.Property.Name != "ChangeTracker") return false;

        return TryGetSymbol(propRef.Instance, out contextSymbol);
    }

    private static bool TryResolveInvocationContext(IInvocationOperation invocation, out ISymbol? contextSymbol)
    {
        contextSymbol = null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
            return TryGetSymbol(invocation.Instance, out contextSymbol);

        if (invocation.TargetMethod.ContainingType.IsDbSet())
        {
            var dbSetInstance = invocation.Instance?.UnwrapConversions();
            switch (dbSetInstance)
            {
                case IPropertyReferenceOperation propRef when propRef.Type.IsDbSet():
                    return TryGetSymbol(propRef.Instance, out contextSymbol);
                case IFieldReferenceOperation fieldRef when fieldRef.Type.IsDbSet():
                    return TryGetSymbol(fieldRef.Instance, out contextSymbol);
                case IInvocationOperation setCall when setCall.TargetMethod.Name == "Set" &&
                                                      setCall.TargetMethod.ContainingType.IsDbContext():
                    return TryGetSymbol(setCall.Instance, out contextSymbol);
            }
        }

        return false;
    }
}
