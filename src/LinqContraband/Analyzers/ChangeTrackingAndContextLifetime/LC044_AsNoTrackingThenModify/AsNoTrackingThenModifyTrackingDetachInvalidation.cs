using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool HasInterveningDetach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        IOperation save)
    {
        var saveSpan = save.Syntax.SpanStart;
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            for (var i = 0; i < detaches.Count; i++)
            {
                var entry = detaches[i];
                if (entry.SpanStart <= afterSpan || entry.SpanStart >= saveSpan) continue;

                if (entry.ContextSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                    BlockReaches(entry.Operation, save))
                {
                    return true;
                }
            }
        }

        var clears = scan.TrackerClears;
        for (var i = 0; i < clears.Count; i++)
        {
            var entry = clears[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= saveSpan) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                BlockReaches(entry.Operation, save))
            {
                return true;
            }
        }

        return false;
    }
}
