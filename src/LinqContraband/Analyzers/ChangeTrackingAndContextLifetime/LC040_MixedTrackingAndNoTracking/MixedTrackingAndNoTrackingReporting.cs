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
    }
}
