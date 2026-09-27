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
                        var current = saves[i];

                        // A save in a branch that always returns or throws, or in a branch exclusive with the
                        // later save, never precedes it; compare with the nearest earlier save that can.
                        var previousIndex = i - 1;
                        while (previousIndex >= 0 &&
                               (LeavesMethodBefore(saves[previousIndex].Syntax, current.Syntax, saves[previousIndex].Root.SemanticModel) ||
                                AreMutuallyExclusiveBranches(saves[previousIndex].Syntax, current.Syntax)))
                            previousIndex--;
                        if (previousIndex < 0)
                            continue;

                        var previous = saves[previousIndex];

                        if (HasTransactionBoundaryBetween(boundaries, previous.Position, current.Position))
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
