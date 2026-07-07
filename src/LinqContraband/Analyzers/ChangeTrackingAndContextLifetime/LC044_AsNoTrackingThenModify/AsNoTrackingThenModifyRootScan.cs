using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

/// <summary>
/// Per-executable-root scan that pre-buckets the operations LC044 needs (initialized declarators,
/// foreach loops, property mutations by local, reattach calls by local, and SaveChanges calls).
/// Cached via <see cref="ConditionalWeakTable{TKey,TValue}"/> so the scan only runs once per root.
/// </summary>
internal sealed partial class AsNoTrackingThenModifyRootScan
{
    private static readonly ConditionalWeakTable<IOperation, AsNoTrackingThenModifyRootScan> Cache = new();

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
    public Dictionary<ILocalSymbol, List<DetachEntry>> DetachesByLocal { get; }
        = new(SymbolEqualityComparer.Default);
    public List<TrackerClearEntry> TrackerClears { get; } = new();
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

            case ICompoundAssignmentOperation compound:
                HandlePropertyMutation(scan, compound, compound.Target);
                break;

            case IIncrementOrDecrementOperation incrementOrDecrement:
                HandlePropertyMutation(scan, incrementOrDecrement, incrementOrDecrement.Target);
                break;

            case IInvocationOperation invocation:
                HandleInvocation(scan, invocation, ref saves);
                break;
        }
    }

}
