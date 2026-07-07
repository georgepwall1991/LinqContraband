using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

public sealed partial class DisposedContextQueryAnalyzer
{
    private static bool TryResolveAssignedDisposedContextOrigin(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        dbContextLocal = null!;

        if (TryGetSingleAssignedValue(local, position, executableRoot, out var assignedValue))
            return TryResolveDisposedContextOrigin(assignedValue, executableRoot, visitedLocals, out dbContextLocal);

        return TryGetSharedAssignedDisposedContextOrigin(local, position, executableRoot, visitedLocals,
            out dbContextLocal);
    }

    private static bool TryGetSharedAssignedDisposedContextOrigin(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        dbContextLocal = null!;
        var assignments = GetAssignedValues(local, position, executableRoot);

        if (assignments.Count <= 1)
            return false;

        foreach (var assignment in assignments)
        {
            if (!TryResolveDisposedContextOrigin(
                    assignment,
                    executableRoot,
                    new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                    out var candidate))
            {
                return false;
            }

            if (dbContextLocal == null)
            {
                dbContextLocal = candidate;
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(dbContextLocal, candidate))
                return false;
        }

        return dbContextLocal != null;
    }

}
