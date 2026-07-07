using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeAnalyzer
{
    private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
    {
        foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Span.Contains(right.SpanStart))
                continue;

            var leftBranch = GetContainingIfBranch(ifStatement, left);
            var rightBranch = GetContainingIfBranch(ifStatement, right);

            if (leftBranch != null && rightBranch != null && leftBranch != rightBranch)
                return true;
        }

        foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
        {
            if (!switchStatement.Span.Contains(right.SpanStart))
                continue;

            // goto case / goto default lets one section flow into another, so sections of
            // a switch containing any such jump are not provably exclusive.
            if (switchStatement.DescendantNodes().Any(node =>
                    node.IsKind(SyntaxKind.GotoCaseStatement) || node.IsKind(SyntaxKind.GotoDefaultStatement)))
            {
                continue;
            }

            var leftSection = GetContainingSwitchSection(switchStatement, left);
            var rightSection = GetContainingSwitchSection(switchStatement, right);

            if (leftSection != null && rightSection != null && leftSection != rightSection)
                return true;
        }

        return false;
    }

    private static SyntaxNode? GetContainingIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ifStatement.Statement.Span.Contains(node.Span))
            return ifStatement.Statement;

        var elseClause = ifStatement.Else;
        return elseClause != null && elseClause.Span.Contains(node.Span) ? elseClause : null;
    }

    private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
    {
        foreach (var section in switchStatement.Sections)
        {
            if (section.Span.Contains(node.Span))
                return section;
        }

        return null;
    }
}
