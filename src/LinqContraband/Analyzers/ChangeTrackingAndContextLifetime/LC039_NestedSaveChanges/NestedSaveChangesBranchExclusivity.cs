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

        /// <summary>
        /// <c>if (entity is null) { db.Add(e); await db.SaveChangesAsync(); return 1; } entity.Count++; await db.SaveChangesAsync();</c>:
        /// the first save sits in a branch that always leaves the method, so the later save never runs after it.
        /// </summary>
        private static bool LeavesMethodBefore(SyntaxNode left, SyntaxNode right)
        {
            foreach (var ifStatement in left.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Span.Contains(right.SpanStart))
                    return false;

                var branch = GetContainingBranch(ifStatement, left);
                if (branch != null && EndsWithMethodExit(branch, right) && !IsInFinallyAround(left, right))
                    return true;
            }

            return false;
        }

        private static bool EndsWithMethodExit(StatementSyntax branch, SyntaxNode right)
        {
            var last = GetLastStatement(branch);
            if (last is ReturnStatementSyntax)
                return true;

            return IsThrow(last) && !IsCaughtBefore(branch, right);
        }

        private static StatementSyntax? GetLastStatement(StatementSyntax statement)
        {
            return statement is BlockSyntax block ? block.Statements.LastOrDefault() : statement;
        }

        private static bool IsThrow(StatementSyntax? statement)
        {
            return statement is ThrowStatementSyntax ||
                   statement is ExpressionStatementSyntax { Expression: ThrowExpressionSyntax };
        }

        /// <summary>
        /// <c>catch { return; }</c> or <c>catch { throw; }</c> never resumes after the try statement.
        /// </summary>
        private static bool CanCompleteNormally(CatchClauseSyntax catchClause)
        {
            var last = GetLastStatement(catchClause.Block);
            return last is not ReturnStatementSyntax && !IsThrow(last);
        }

        /// <summary>
        /// <c>try { if (flag) { db.SaveChanges(); throw ...; } } catch { } db.SaveChanges();</c>: the catch swallows the
        /// throw and the later save still runs. Only a try whose try block also holds the later save is skipped by the throw,
        /// and a try whose every catch returns or throws does not resume.
        /// </summary>
        private static bool IsCaughtBefore(SyntaxNode branch, SyntaxNode right)
        {
            foreach (var ancestor in branch.Ancestors())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
                    return false;

                if (ancestor is TryStatementSyntax tryStatement &&
                    tryStatement.Catches.Any(CanCompleteNormally) &&
                    tryStatement.Block.Span.Contains(branch.SpanStart) &&
                    !tryStatement.Block.Span.Contains(right.SpanStart))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInFinallyAround(SyntaxNode left, SyntaxNode right)
        {
            return left.Ancestors().OfType<TryStatementSyntax>().Any(tryStatement =>
                tryStatement.Finally?.Block.Span.Contains(right.SpanStart) == true);
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
