using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

/// <summary>
/// Per-executable-root scan that pre-buckets the operations LC044 needs (initialized declarators,
/// foreach loops, property mutations by local, reattach calls by local, and SaveChanges calls).
/// Cached via <see cref="ConditionalWeakTable{TKey,TValue}"/> so the scan only runs once per root.
/// </summary>
internal sealed class AsNoTrackingThenModifyRootScan
{
    private static readonly ConditionalWeakTable<IOperation, AsNoTrackingThenModifyRootScan> Cache = new();

    private static readonly ImmutableHashSet<string> ReattachMethodNames = ImmutableHashSet.Create(
        "Update", "UpdateRange", "Attach", "AttachRange");

    private static readonly ImmutableHashSet<string> TrackingStates = ImmutableHashSet.Create(
        "Modified", "Added");

    private static readonly List<IVariableDeclaratorOperation> EmptyDeclarators = new();
    private static readonly List<IForEachLoopOperation> EmptyForEachLoops = new();
    private static readonly List<SaveChangesEntry> EmptySaveChanges = new();

    private AsNoTrackingThenModifyRootScan() { }

    public List<IVariableDeclaratorOperation> InitializedDeclarators { get; private set; } = EmptyDeclarators;
    public List<IForEachLoopOperation> ForEachLoops { get; private set; } = EmptyForEachLoops;
    public Dictionary<ILocalSymbol, List<MutationEntry>> MutationsByLocal { get; }
        = new(SymbolEqualityComparer.Default);
    public Dictionary<ILocalSymbol, List<ReattachEntry>> ReattachesByLocal { get; }
        = new(SymbolEqualityComparer.Default);
    public List<SaveChangesEntry> SaveChangesCalls { get; private set; } = EmptySaveChanges;

    public static AsNoTrackingThenModifyRootScan GetOrBuild(IOperation root, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(root, out var existing))
            return existing;

        var scan = Build(root, cancellationToken);
        try
        {
            Cache.Add(root, scan);
        }
        catch (ArgumentException)
        {
            if (Cache.TryGetValue(root, out var raced))
                return raced;
        }

        return scan;
    }

    private static AsNoTrackingThenModifyRootScan Build(IOperation root, CancellationToken cancellationToken)
    {
        var scan = new AsNoTrackingThenModifyRootScan();
        List<IVariableDeclaratorOperation>? decls = null;
        List<IForEachLoopOperation>? loops = null;
        List<SaveChangesEntry>? saves = null;

        Visit(root, scan, ref decls, ref loops, ref saves, cancellationToken);

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Visit(descendant, scan, ref decls, ref loops, ref saves, cancellationToken);
        }

        scan.InitializedDeclarators = decls ?? EmptyDeclarators;
        scan.ForEachLoops = loops ?? EmptyForEachLoops;
        scan.SaveChangesCalls = saves ?? EmptySaveChanges;
        return scan;
    }

    private static void Visit(
        IOperation op,
        AsNoTrackingThenModifyRootScan scan,
        ref List<IVariableDeclaratorOperation>? decls,
        ref List<IForEachLoopOperation>? loops,
        ref List<SaveChangesEntry>? saves,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (op)
        {
            case IVariableDeclaratorOperation declarator when declarator.Initializer?.Value != null:
                decls ??= new List<IVariableDeclaratorOperation>();
                decls.Add(declarator);
                break;

            case IForEachLoopOperation forEach:
                loops ??= new List<IForEachLoopOperation>();
                loops.Add(forEach);
                break;

            case ISimpleAssignmentOperation assignment:
                HandleAssignment(scan, assignment);
                break;

            case IInvocationOperation invocation:
                HandleInvocation(scan, invocation, ref saves);
                break;
        }
    }

    private static void HandleAssignment(AsNoTrackingThenModifyRootScan scan, ISimpleAssignmentOperation assignment)
    {
        if (assignment.Target is IPropertyReferenceOperation propRef &&
            propRef.Instance?.UnwrapConversions() is ILocalReferenceOperation instanceLocal)
        {
            var entry = new MutationEntry(
                assignment,
                propRef.Syntax.GetLocation(),
                propRef.Property.Name,
                assignment.Syntax.SpanStart);
            AddMutation(scan, instanceLocal.Local, entry);
        }

        if (TryParseEntryStateReattach(assignment, out var entryLocal, out var entryContext))
        {
            AddReattach(
                scan,
                entryLocal,
                new ReattachEntry(entryContext, assignment.Syntax.SpanStart, assignment.Syntax.Span));
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
                new ReattachEntry(reattachContext, invocation.Syntax.SpanStart, invocation.Syntax.Span));
        }
    }

    private static bool TryParseReattachInvocation(
        IInvocationOperation invocation,
        out ILocalSymbol local,
        out ISymbol? contextSymbol)
    {
        local = null!;
        contextSymbol = null;

        if (!ReattachMethodNames.Contains(invocation.TargetMethod.Name)) return false;

        var container = invocation.TargetMethod.ContainingType;
        if (!container.IsDbContext() && !container.IsDbSet()) return false;

        if (invocation.Arguments.Length == 0) return false;
        if (invocation.Arguments[0].Value.UnwrapConversions() is not ILocalReferenceOperation argLocal)
            return false;

        if (!TryResolveInvocationContext(invocation, out contextSymbol)) return false;

        local = argLocal.Local;
        return true;
    }

    private static bool TryParseEntryStateReattach(
        ISimpleAssignmentOperation assignment,
        out ILocalSymbol local,
        out ISymbol? contextSymbol)
    {
        local = null!;
        contextSymbol = null;

        if (assignment.Target is not IPropertyReferenceOperation targetProp) return false;
        if (targetProp.Property.Name != "State") return false;
        if (targetProp.Property.ContainingType?.Name != "EntityEntry") return false;

        if (targetProp.Instance is not IInvocationOperation entryInv) return false;
        if (entryInv.TargetMethod.Name != "Entry") return false;
        if (!entryInv.TargetMethod.ContainingType.IsDbContext()) return false;

        if (entryInv.Arguments.Length == 0) return false;
        if (entryInv.Arguments[0].Value.UnwrapConversions() is not ILocalReferenceOperation argLocal) return false;

        if (!TryGetSymbol(entryInv.Instance, out contextSymbol)) return false;

        var value = assignment.Value.UnwrapConversions();
        if (value is not IFieldReferenceOperation fieldRef) return false;
        if (fieldRef.Field.ContainingType?.Name != "EntityState") return false;
        if (!TrackingStates.Contains(fieldRef.Field.Name)) return false;

        local = argLocal.Local;
        return true;
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
        if (!scan.MutationsByLocal.TryGetValue(local, out var list))
        {
            list = new List<MutationEntry>();
            scan.MutationsByLocal[local] = list;
        }

        list.Add(entry);
    }

    private static void AddReattach(AsNoTrackingThenModifyRootScan scan, ILocalSymbol local, ReattachEntry entry)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var list))
        {
            list = new List<ReattachEntry>();
            scan.ReattachesByLocal[local] = list;
        }

        list.Add(entry);
    }
}

internal readonly struct MutationEntry
{
    public MutationEntry(IOperation operation, Location targetLocation, string propertyName, int spanStart)
    {
        Operation = operation;
        TargetLocation = targetLocation;
        PropertyName = propertyName;
        SpanStart = spanStart;
    }

    public IOperation Operation { get; }
    public Location TargetLocation { get; }
    public string PropertyName { get; }
    public int SpanStart { get; }
}

internal readonly struct ReattachEntry
{
    public ReattachEntry(ISymbol? contextSymbol, int spanStart, TextSpan span)
    {
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
        Span = span;
    }

    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
    public TextSpan Span { get; }
}

internal readonly struct SaveChangesEntry
{
    public SaveChangesEntry(ISymbol? contextSymbol, int spanStart)
    {
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
    }

    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
}
