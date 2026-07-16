using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
                        persistsExistingMutation: true,
                        coversDescendantPaths: false,
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
        out ImmutableArray<MemberPathSegment> memberPath)
    {
        var reversedPath = ImmutableArray.CreateBuilder<MemberPathSegment>();
        var current = instance?.UnwrapConversions();
        while (current != null)
        {
            switch (current)
            {
                case IPropertyReferenceOperation propertyReference
                    when !HasNotMappedAttribute(propertyReference.Property):
                    reversedPath.Add(CreateMemberPathSegment(propertyReference));
                    current = propertyReference.Instance?.UnwrapConversions();
                    continue;

                case ILocalReferenceOperation localReference:
                    rootLocal = localReference.Local;
                    var path = ImmutableArray.CreateBuilder<MemberPathSegment>(reversedPath.Count);
                    for (var i = reversedPath.Count - 1; i >= 0; i--)
                        path.Add(reversedPath[i]);
                    memberPath = path.MoveToImmutable();
                    return true;

                default:
                    rootLocal = null!;
                    memberPath = ImmutableArray<MemberPathSegment>.Empty;
                    return false;
            }
        }

        rootLocal = null!;
        memberPath = ImmutableArray<MemberPathSegment>.Empty;
        return false;
    }

    private static MemberPathSegment CreateMemberPathSegment(IPropertyReferenceOperation propertyReference)
    {
        if (!propertyReference.Property.IsIndexer)
            return new MemberPathSegment(propertyReference.Property, isIndexer: false, indexKey: null);

        var keyParts = new List<string>(propertyReference.Arguments.Length);
        foreach (var argument in propertyReference.Arguments)
        {
            var constant = argument.Value.ConstantValue;
            if (!constant.HasValue)
                return new MemberPathSegment(propertyReference.Property, isIndexer: true, indexKey: null);

            var typeName = argument.Value.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
            var valueText = constant.Value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => constant.Value.ToString()
            };
            keyParts.Add(EncodeIndexKeyPart(typeName, valueText));
        }

        return new MemberPathSegment(
            propertyReference.Property,
            isIndexer: true,
            indexKey: string.Join("|", keyParts));
    }

    private static string EncodeIndexKeyPart(string typeName, string? valueText)
    {
        var encodedValue = valueText == null
            ? "N"
            : "V" + valueText.Length.ToString(CultureInfo.InvariantCulture) + ":" + valueText;
        return typeName.Length.ToString(CultureInfo.InvariantCulture) + ":" + typeName + encodedValue;
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
                out var reattachContext,
                out var persistsExistingMutation))
        {
            foreach (var argument in invocation.Arguments)
            {
                RecordReattachTargets(
                    scan,
                    invocation,
                    argument.Value,
                    reattachContext,
                    persistsExistingMutation);
            }
        }

        if (TryParseTrackerClear(invocation, out var clearContext))
        {
            scan.TrackerClears.Add(new TrackerClearEntry(invocation, clearContext, invocation.Syntax.SpanStart));
        }
    }

    private static void RecordReattachTargets(
        AsNoTrackingThenModifyRootScan scan,
        IInvocationOperation invocation,
        IOperation value,
        ISymbol? contextSymbol,
        bool persistsExistingMutation)
    {
        var unwrappedValue = value.UnwrapConversions();
        if (unwrappedValue is IArrayCreationOperation { Initializer: { } initializer })
        {
            foreach (var element in initializer.ElementValues)
            {
                RecordReattachTargets(
                    scan,
                    invocation,
                    element,
                    contextSymbol,
                    persistsExistingMutation);
            }

            return;
        }

        if (!TryGetRootLocalAndMemberPath(unwrappedValue, out var local, out var targetPath))
            return;

        AddReattach(
            scan,
            local,
            new ReattachEntry(
                invocation,
                contextSymbol,
                targetPath,
                persistsExistingMutation,
                coversDescendantPaths: true,
                invocation.Syntax.SpanStart,
                invocation.Syntax.Span));
    }
}
