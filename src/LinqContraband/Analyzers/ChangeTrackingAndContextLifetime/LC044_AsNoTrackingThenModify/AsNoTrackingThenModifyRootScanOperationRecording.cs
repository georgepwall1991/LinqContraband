using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal sealed partial class AsNoTrackingThenModifyRootScan
{
    private static void HandleAssignment(AsNoTrackingThenModifyRootScan scan, ISimpleAssignmentOperation assignment)
    {
        HandlePropertyMutation(scan, assignment, assignment.Target);

        if (TryParseEntryStateAssignment(assignment, out var entryLocal, out var entryContext, out var stateName))
        {
            if (TrackingStates.Contains(stateName))
            {
                AddReattach(
                    scan,
                    entryLocal,
                    new ReattachEntry(assignment, entryContext, assignment.Syntax.SpanStart, assignment.Syntax.Span));
            }
            else if (stateName == "Detached")
            {
                AddDetach(
                    scan,
                    entryLocal,
                    new DetachEntry(assignment, entryContext, assignment.Syntax.SpanStart, assignment.Syntax.Span));
            }
        }
    }

    private static void HandlePropertyMutation(AsNoTrackingThenModifyRootScan scan, IOperation mutation, IOperation? target)
    {
        if (target is IPropertyReferenceOperation propRef &&
            propRef.Instance?.UnwrapConversions() is ILocalReferenceOperation instanceLocal)
        {
            AddMutation(scan, instanceLocal.Local, new MutationEntry(
                mutation,
                propRef.Syntax.GetLocation(),
                propRef.Property.Name,
                mutation.Syntax.SpanStart));
        }
    }

    private static void HandleInvocation(
        AsNoTrackingThenModifyRootScan scan,
        IInvocationOperation invocation,
        ref List<SaveChangesEntry>? saves)
    {
        var method = invocation.TargetMethod;

        if ((method.Name == "SaveChanges" || method.Name == "SaveChangesAsync") &&
            method.ContainingType.IsDbContext())
        {
            if (TryGetSymbol(invocation.Instance, out var sym))
            {
                saves ??= new List<SaveChangesEntry>();
                saves.Add(new SaveChangesEntry(sym, invocation.Syntax.SpanStart));
            }
        }

        if (TryParseReattachInvocation(invocation, out var reattachLocal, out var reattachContext))
        {
            AddReattach(
                scan,
                reattachLocal,
                new ReattachEntry(invocation, reattachContext, invocation.Syntax.SpanStart, invocation.Syntax.Span));
        }

        if (TryParseTrackerClear(invocation, out var clearContext))
        {
            scan.TrackerClears.Add(new TrackerClearEntry(invocation, clearContext, invocation.Syntax.SpanStart));
        }
    }
}
