using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var groupedByRoot = _records
                .GroupBy(record => record.Root, OperationRootComparer.Instance)
                .ToArray();

            foreach (var rootGroup in groupedByRoot)
            {
                foreach (var contextGroup in rootGroup
                             .Where(record => record.ContextSymbol != null)
                             .GroupBy(record => record.ContextSymbol!, SymbolEqualityComparer.Default))
                {
                    var records = contextGroup
                        .OrderBy(record => record.Position)
                        .ToArray();

                    if (records.Length < 2)
                        continue;

                    var reported = false;

                    for (var i = 1; i < records.Length; i++)
                    {
                        var current = records[i];
                        if (reported)
                            continue;

                        for (var previousIndex = 0; previousIndex < i; previousIndex++)
                        {
                            var previous = records[previousIndex];
                            if (previous.Mode == current.Mode ||
                                !AreInSameEntityHierarchy(previous.EntityType, current.EntityType) ||
                                AreMutuallyExclusiveBranches(previous.Syntax, current.Syntax))
                            {
                                continue;
                            }

                            context.ReportDiagnostic(
                                Diagnostic.Create(Rule, current.Location, contextGroup.Key.Name));
                            reported = true;
                            break;
                        }
                    }
                }
            }
        }

        // Tracked AclRecords next to an AsNoTracking() CustomerRole lookup cannot conflict: no entity can be both a
        // tracked and an untracked instance. Only reads over one entity type, or over a base and a derived type
        // (which can return the same rows), mix tracking modes for the same entities.
        private static bool AreInSameEntityHierarchy(ITypeSymbol? left, ITypeSymbol? right)
        {
            if (left == null || right == null)
                return false;

            return DerivesFromOrEquals(left, right) || DerivesFromOrEquals(right, left);
        }

        private static bool DerivesFromOrEquals(ITypeSymbol type, ITypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
            }

            return false;
        }
    }
}
