using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
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
                var boundaries = rootGroup
                    .Where(record => record.IsBoundary)
                    .Select(record => record.Position)
                    .OrderBy(position => position)
                    .ToArray();

                var savesByContext = rootGroup
                    .Where(record => !record.IsBoundary && record.ContextSymbol != null)
                    .GroupBy(record => record.ContextSymbol!, SymbolEqualityComparer.Default);

                foreach (var contextGroup in savesByContext)
                {
                    var saves = contextGroup
                        .OrderBy(record => record.Position)
                        .ToArray();

                    if (saves.Length < 2)
                        continue;

                    for (var i = 1; i < saves.Length; i++)
                    {
                        var previous = saves[i - 1];
                        var current = saves[i];

                        if (HasTransactionBoundaryBetween(boundaries, previous.Position, current.Position))
                            continue;

                        if (AreMutuallyExclusiveBranches(previous.Syntax, current.Syntax))
                            continue;

                        if (AreInsideSameTransactionUsing(previous.Syntax, current.Syntax, boundaries))
                            continue;

                        Report(context, current, contextGroup.Key, current.MethodName);
                    }
                }
            }
        }

        private static void Report(CompilationAnalysisContext context, InvocationRecord record, ISymbol contextSymbol, string methodName)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, record.Location, contextSymbol.Name, methodName));
        }
    }
}
