using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// Per-executable-root cache of local variable assignments and declarator initializers.
/// The cache is keyed by the executable root <see cref="IOperation"/> and lives only as long
/// as that operation is reachable, so it is automatically released between compilations.
/// </summary>
internal static class LocalAssignmentCache
{
    private static readonly ConditionalWeakTable<IOperation, RootScan> Cache = new();

    public static IReadOnlyList<LocalAssignment> GetAssignments(
        IOperation executableRoot,
        ILocalSymbol local,
        CancellationToken cancellationToken = default)
    {
        var scan = GetOrAdd(executableRoot, cancellationToken);
        return scan.GetAssignments(local);
    }

    public static bool TryGetSingleAssignedValueBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition,
        out IOperation value,
        CancellationToken cancellationToken = default)
    {
        value = null!;
        var assignments = GetAssignments(executableRoot, local, cancellationToken);
        if (assignments.Count == 0) return false;

        IOperation? latest = null;
        var latestSpan = -1;
        var matchCount = 0;

        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            if (assignment.SpanStart >= beforePosition) continue;

            matchCount++;
            if (assignment.SpanStart > latestSpan)
            {
                latestSpan = assignment.SpanStart;
                latest = assignment.Value;
            }
        }

        if (matchCount != 1 || latest == null) return false;

        value = latest.UnwrapConversions();
        return true;
    }

    private static RootScan GetOrAdd(IOperation executableRoot, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0 || NETSTANDARD2_1
        if (Cache.TryGetValue(executableRoot, out var existing))
            return existing;

        var scan = RootScan.Build(executableRoot, cancellationToken);
        try
        {
            Cache.Add(executableRoot, scan);
            return scan;
        }
        catch (ArgumentException)
        {
            return Cache.TryGetValue(executableRoot, out var raced) ? raced : scan;
        }
#else
        return Cache.GetValue(executableRoot, root => RootScan.Build(root, cancellationToken));
#endif
    }

    private sealed class RootScan
    {
        private static readonly IReadOnlyList<LocalAssignment> EmptyAssignments = Array.Empty<LocalAssignment>();

        private readonly Dictionary<ILocalSymbol, List<LocalAssignment>> assignmentsByLocal;

        private RootScan(Dictionary<ILocalSymbol, List<LocalAssignment>> assignmentsByLocal)
        {
            this.assignmentsByLocal = assignmentsByLocal;
        }

        public IReadOnlyList<LocalAssignment> GetAssignments(ILocalSymbol local)
        {
            return assignmentsByLocal.TryGetValue(local, out var list)
                ? list
                : EmptyAssignments;
        }

        public static RootScan Build(IOperation executableRoot, CancellationToken cancellationToken)
        {
            var assignments = new Dictionary<ILocalSymbol, List<LocalAssignment>>(SymbolEqualityComparer.Default);

            foreach (var descendant in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (descendant)
                {
                    case IVariableDeclaratorOperation declarator
                        when declarator.Initializer != null:
                        Add(assignments, declarator.Symbol, declarator.Syntax.SpanStart, declarator.Initializer.Value);
                        break;

                    case ISimpleAssignmentOperation assignment
                        when assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal:
                        Add(assignments, targetLocal.Local, assignment.Syntax.SpanStart, assignment.Value);
                        break;
                }
            }

            return new RootScan(assignments);
        }

        private static void Add(
            Dictionary<ILocalSymbol, List<LocalAssignment>> assignments,
            ILocalSymbol local,
            int spanStart,
            IOperation value)
        {
            if (!assignments.TryGetValue(local, out var list))
            {
                list = new List<LocalAssignment>();
                assignments[local] = list;
            }

            list.Add(new LocalAssignment(spanStart, value));
        }
    }
}

internal readonly struct LocalAssignment
{
    public LocalAssignment(int spanStart, IOperation value)
    {
        SpanStart = spanStart;
        Value = value;
    }

    public int SpanStart { get; }

    public IOperation Value { get; }
}
