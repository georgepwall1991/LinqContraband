using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
        {
            foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
            {
                if (!ifStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftBranch = GetContainingBranch(ifStatement, left);
                var rightBranch = GetContainingBranch(ifStatement, right);

                if (leftBranch != null &&
                    rightBranch != null &&
                    leftBranch != rightBranch)
                {
                    return true;
                }
            }

            foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
            {
                if (!switchStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftSection = GetContainingSwitchSection(switchStatement, left);
                var rightSection = GetContainingSwitchSection(switchStatement, right);

                if (leftSection != null &&
                    rightSection != null &&
                    leftSection != rightSection)
                {
                    return true;
                }
            }

            // A ternary's two arms are mutually exclusive just like if/else: only one materializes
            // at runtime, so `cond ? db.Users.AsNoTracking().ToList() : db.Users.ToList()` never
            // mixes tracking modes. A materializer in the condition (which always runs) returns null
            // from GetContainingTernaryArm and is intentionally not treated as exclusive.
            foreach (var ternary in left.AncestorsAndSelf().OfType<ConditionalExpressionSyntax>())
            {
                if (!ternary.Span.Contains(right.SpanStart))
                    continue;

                var leftArm = GetContainingTernaryArm(ternary, left);
                var rightArm = GetContainingTernaryArm(ternary, right);

                if (leftArm != null &&
                    rightArm != null &&
                    leftArm != rightArm)
                {
                    return true;
                }
            }

            return false;
        }

        private static ExpressionSyntax? GetContainingTernaryArm(ConditionalExpressionSyntax ternary, SyntaxNode node)
        {
            if (ternary.WhenTrue.Span.Contains(node.SpanStart))
                return ternary.WhenTrue;

            if (ternary.WhenFalse.Span.Contains(node.SpanStart))
                return ternary.WhenFalse;

            return null;
        }

        private static StatementSyntax? GetContainingBranch(IfStatementSyntax ifStatement, SyntaxNode node)
        {
            if (ifStatement.Statement.Span.Contains(node.SpanStart))
                return ifStatement.Statement;

            if (ifStatement.Else?.Statement.Span.Contains(node.SpanStart) == true)
                return ifStatement.Else.Statement;

            return null;
        }

        private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
        {
            return switchStatement.Sections.FirstOrDefault(section => section.Span.Contains(node.SpanStart));
        }
    }
}
