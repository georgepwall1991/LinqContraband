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
internal static partial class LocalAssignmentCache
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
