using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
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

            foreach (var switchExpression in left.AncestorsAndSelf().OfType<SwitchExpressionSyntax>())
            {
                if (!switchExpression.Span.Contains(right.SpanStart))
                    continue;

                var leftArm = GetContainingSwitchExpressionArm(switchExpression, left);
                var rightArm = GetContainingSwitchExpressionArm(switchExpression, right);

                if (leftArm != null &&
                    rightArm != null &&
                    leftArm != rightArm)
                {
                    return true;
                }
            }

            // SaveChanges in try and catch branches are mutually exclusive; finally is not exclusive.
            foreach (var tryStatement in left.AncestorsAndSelf().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftTryBranch = GetContainingTryBranch(tryStatement, left);
                var rightTryBranch = GetContainingTryBranch(tryStatement, right);

                if (leftTryBranch != null &&
                    rightTryBranch != null &&
                    leftTryBranch != rightTryBranch)
                {
                    return true;
                }
            }

            return false;
        }

        private static SyntaxNode? GetContainingTryBranch(TryStatementSyntax tryStatement, SyntaxNode node)
        {
            if (tryStatement.Block.Span.Contains(node.SpanStart))
                return tryStatement.Block;

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Block.Span.Contains(node.SpanStart))
                    return catchClause;
            }

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

        private static SwitchExpressionArmSyntax? GetContainingSwitchExpressionArm(SwitchExpressionSyntax switchExpression, SyntaxNode node)
        {
            return switchExpression.Arms.FirstOrDefault(arm => arm.Expression.Span.Contains(node.SpanStart));
        }
    }
}
