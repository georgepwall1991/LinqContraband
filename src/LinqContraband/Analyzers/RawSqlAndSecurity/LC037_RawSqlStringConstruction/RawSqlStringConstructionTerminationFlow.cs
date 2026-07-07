using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static StatementSyntax? GetEnclosingIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ContainsNode(ifStatement.Statement, node))
            return ifStatement.Statement;

        var elseStatement = ifStatement.Else?.Statement;
        return elseStatement != null && ContainsNode(elseStatement, node) ? elseStatement : null;
    }

    private static bool BlockTerminatesAfterNode(BlockSyntax block, SyntaxNode node, int referenceStart)
    {
        for (var i = 0; i < block.Statements.Count; i++)
        {
            var statement = block.Statements[i];
            if (!ContainsNode(statement, node))
                continue;

            return block.Statements
                .Skip(i + 1)
                .Any(statement => StatementExitsMethod(statement, referenceStart));
        }

        return false;
    }

    private static bool SwitchSectionTerminatesAfterNode(
        SwitchSectionSyntax switchSection,
        SyntaxNode node,
        int referenceStart)
    {
        for (var i = 0; i < switchSection.Statements.Count; i++)
        {
            var statement = switchSection.Statements[i];
            if (!ContainsNode(statement, node))
                continue;

            return switchSection.Statements
                .Skip(i + 1)
                .Any(statement => StatementExitsMethod(statement, referenceStart));
        }

        return false;
    }

    private static bool IsInsideOnlySurvivingIfBranches(SyntaxNode node, int referenceStart)
    {
        var sawControllingIf = false;
        foreach (var ifStatement in node.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Span.End > referenceStart)
                continue;

            var branch = GetEnclosingIfBranch(ifStatement, node);
            if (branch == null)
                continue;

            if (HasPotentiallySkippingAncestor(ifStatement, referenceStart))
                return false;

            var otherBranch = ReferenceEquals(branch, ifStatement.Statement)
                ? ifStatement.Else?.Statement
                : ifStatement.Statement;

            if (!StatementTerminates(otherBranch, node, referenceStart))
                return false;

            sawControllingIf = true;
        }

        return sawControllingIf;
    }

    private static bool StatementTerminates(StatementSyntax? statement, SyntaxNode survivingNode, int referenceStart)
    {
        return statement switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax => true,
            ContinueStatementSyntax continueStatement => JumpSkipsReference(continueStatement, survivingNode, referenceStart, allowSwitch: false),
            BreakStatementSyntax breakStatement => JumpSkipsReference(breakStatement, survivingNode, referenceStart, allowSwitch: true),
            BlockSyntax block when block.Statements.Count > 0 => StatementTerminates(block.Statements.Last(), survivingNode, referenceStart),
            IfStatementSyntax ifStatement when ifStatement.Else != null =>
                StatementTerminates(ifStatement.Statement, survivingNode, referenceStart) &&
                StatementTerminates(ifStatement.Else.Statement, survivingNode, referenceStart),
            _ => false
        };
    }

    private static bool StatementExitsMethod(StatementSyntax? statement, int referenceStart)
    {
        return statement switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax throwStatement => !ThrowCanContinueThroughCatch(throwStatement, referenceStart),
            BlockSyntax block when block.Statements.Count > 0 => StatementExitsMethod(block.Statements.Last(), referenceStart),
            IfStatementSyntax ifStatement when ifStatement.Else != null =>
                StatementExitsMethod(ifStatement.Statement, referenceStart) &&
                StatementExitsMethod(ifStatement.Else.Statement, referenceStart),
            _ => false
        };
    }

}
