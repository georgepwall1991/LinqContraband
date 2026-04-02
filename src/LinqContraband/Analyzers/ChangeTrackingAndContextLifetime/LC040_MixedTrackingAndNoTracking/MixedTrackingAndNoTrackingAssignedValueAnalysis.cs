using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool TryResolveAssignedValue(ILocalSymbol local, IOperation root, out IOperation? assignedValue)
        {
            assignedValue = null;
            var matches = 0;

            foreach (var descendant in root.Descendants())
            {
                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                    SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
                {
                    matches++;
                    assignedValue = assignment.Value;
                }
                else if (descendant is IVariableDeclaratorOperation declarator &&
                         SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null)
                {
                    matches++;
                    assignedValue = declarator.Initializer.Value;
                }

                if (matches > 1)
                    return false;
            }

            return matches == 1 && assignedValue != null;
        }
    }
}
