using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool TryResolveAssignedValue(ILocalReferenceOperation localReference, IOperation root, out IOperation? assignedValue)
        {
            assignedValue = null;
            var assignments = LocalAssignmentCache.GetAssignments(root, localReference.Local);
            LocalAssignment? latest = null;

            foreach (var assignment in assignments)
            {
                if (assignment.SpanStart >= localReference.Syntax.SpanStart)
                    continue;

                if (latest == null || assignment.SpanStart > latest.Value.SpanStart)
                    latest = assignment;
            }

            if (latest == null || IsControlFlowConditionalAssignment(latest.Value.Value.Syntax))
                return false;

            assignedValue = latest.Value.Value.UnwrapConversions();
            return true;
        }

        private static bool IsControlFlowConditionalAssignment(SyntaxNode syntax)
        {
            return syntax.Ancestors().Any(ancestor =>
                ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
                    ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                    TryStatementSyntax or CatchClauseSyntax);
        }
    }
}
