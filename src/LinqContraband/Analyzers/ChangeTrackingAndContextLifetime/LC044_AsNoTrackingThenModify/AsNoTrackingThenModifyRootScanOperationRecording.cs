using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal sealed partial class AsNoTrackingThenModifyRootScan
{
    private static void HandleAssignment(AsNoTrackingThenModifyRootScan scan, ISimpleAssignmentOperation assignment)
    {
        HandlePropertyMutation(scan, assignment, assignment.Target);

        if (TryParseEntryStateAssignment(
                assignment,
                out var entryLocal,
                out var entryContext,
                out var entryTargetPath,
                out var stateName))
        {
            if (TrackingStates.Contains(stateName))
            {
                AddReattach(
                    scan,
                    entryLocal,
                    new ReattachEntry(
                        assignment,
                        entryContext,
                        entryTargetPath,
                        assignment.Syntax.SpanStart,
                        assignment.Syntax.Span));
            }
            else if (stateName == "Detached")
            {
                AddDetach(
                    scan,
                    entryLocal,
                    new DetachEntry(
                        assignment,
                        entryContext,
                        entryTargetPath,
                        assignment.Syntax.SpanStart,
                        assignment.Syntax.Span));
            }
        }
    }

    private static void HandlePropertyMutation(AsNoTrackingThenModifyRootScan scan, IOperation mutation, IOperation? target)
    {
        if (target is IPropertyReferenceOperation propRef &&
            !HasNotMappedAttribute(propRef.Property) &&
            TryGetRootLocalAndMemberPath(propRef.Instance, out var rootLocal, out var receiverPath))
        {
            AddMutation(scan, rootLocal, new MutationEntry(
                mutation,
                propRef.Syntax.GetLocation(),
                propRef.Property.Name,
                receiverPath,
                mutation.Syntax.SpanStart));
        }
    }

    private static bool TryGetRootLocalAndMemberPath(
        IOperation? instance,
        out ILocalSymbol rootLocal,
        out ImmutableArray<ISymbol> memberPath)
    {
        var reversedPath = ImmutableArray.CreateBuilder<ISymbol>();
        var current = instance?.UnwrapConversions();
        while (current != null)
        {
            switch (current)
            {
                case IPropertyReferenceOperation propertyReference
                    when !HasNotMappedAttribute(propertyReference.Property):
                    reversedPath.Add(propertyReference.Property);
                    current = propertyReference.Instance?.UnwrapConversions();
                    continue;

                case IFieldReferenceOperation fieldReference
                    when !HasNotMappedAttribute(fieldReference.Field):
                    reversedPath.Add(fieldReference.Field);
                    current = fieldReference.Instance?.UnwrapConversions();
                    continue;

                case ILocalReferenceOperation localReference:
                    rootLocal = localReference.Local;
                    var path = ImmutableArray.CreateBuilder<ISymbol>(reversedPath.Count);
                    for (var i = reversedPath.Count - 1; i >= 0; i--)
                        path.Add(reversedPath[i]);
                    memberPath = path.MoveToImmutable();
                    return true;

                default:
                    rootLocal = null!;
                    memberPath = ImmutableArray<ISymbol>.Empty;
                    return false;
            }
        }

        rootLocal = null!;
        memberPath = ImmutableArray<ISymbol>.Empty;
        return false;
    }

    private static bool HasNotMappedAttribute(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() ==
                "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute")
            {
                return true;
            }
        }

        return false;
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
                saves.Add(new SaveChangesEntry(invocation, sym, invocation.Syntax.SpanStart));
            }
        }

        if (TryParseReattachInvocation(
                invocation,
                out var reattachLocal,
                out var reattachContext,
                out var reattachTargetPath))
        {
            AddReattach(
                scan,
                reattachLocal,
                new ReattachEntry(
                    invocation,
                    reattachContext,
                    reattachTargetPath,
                    invocation.Syntax.SpanStart,
                    invocation.Syntax.Span));
        }

        if (TryParseTrackerClear(invocation, out var clearContext))
        {
            scan.TrackerClears.Add(new TrackerClearEntry(invocation, clearContext, invocation.Syntax.SpanStart));
        }
    }
}
