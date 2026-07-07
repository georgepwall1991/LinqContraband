using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool IsNestedUnderOptionalControlFlow(IOperation operation, IBlockOperation enclosingBlock, IOperation later)
    {
        for (var ancestor = operation.Syntax.Parent; ancestor != null && !ReferenceEquals(ancestor, enclosingBlock.Syntax); ancestor = ancestor.Parent)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (IfStatementMakesBranchMandatory(ifStatement, operation.Syntax, later.Syntax))
                    continue;

                return true;
            }

            if (ancestor.IsKind(SyntaxKind.ElseClause))
                continue;

            if (
                ancestor.IsKind(SyntaxKind.SwitchStatement) ||
                ancestor.IsKind(SyntaxKind.SwitchSection) ||
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.DoStatement) ||
                ancestor.IsKind(SyntaxKind.ForStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachVariableStatement) ||
                ancestor.IsKind(SyntaxKind.ConditionalExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IfStatementMakesBranchMandatory(
        IfStatementSyntax ifStatement,
        SyntaxNode operationSyntax,
        SyntaxNode laterSyntax)
    {
        if (laterSyntax.SpanStart <= ifStatement.Span.End) return false;

        var operationStart = operationSyntax.SpanStart;
        if (ifStatement.Statement.Span.Contains(operationStart))
            return ifStatement.Else?.Statement is { } elseStatement && StatementSkipsLater(elseStatement, laterSyntax);

        if (ifStatement.Else?.Statement.Span.Contains(operationStart) == true)
            return StatementSkipsLater(ifStatement.Statement, laterSyntax);

        return false;
    }

    private static bool StatementSkipsLater(StatementSyntax statement, SyntaxNode laterSyntax)
    {
        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;

            case BlockSyntax block:
                return block.Statements.Count > 0 && StatementSkipsLater(block.Statements[block.Statements.Count - 1], laterSyntax);

            case IfStatementSyntax ifStatement:
                return ifStatement.Else?.Statement is { } elseStatement &&
                       StatementSkipsLater(ifStatement.Statement, laterSyntax) &&
                       StatementSkipsLater(elseStatement, laterSyntax);

            case BreakStatementSyntax breakStatement:
                return BranchSkipsLater(breakStatement, laterSyntax, includeSwitch: true);

            case ContinueStatementSyntax continueStatement:
                return BranchSkipsLater(continueStatement, laterSyntax, includeSwitch: false);

            default:
                return false;
        }
    }

    private static bool BranchSkipsLater(StatementSyntax branchStatement, SyntaxNode laterSyntax, bool includeSwitch)
    {
        var enclosingConstruct = branchStatement.Ancestors().FirstOrDefault(a =>
            a.IsKind(SyntaxKind.WhileStatement) ||
            a.IsKind(SyntaxKind.DoStatement) ||
            a.IsKind(SyntaxKind.ForStatement) ||
            a.IsKind(SyntaxKind.ForEachStatement) ||
            a.IsKind(SyntaxKind.ForEachVariableStatement) ||
            (includeSwitch && a.IsKind(SyntaxKind.SwitchStatement)));

        return enclosingConstruct != null &&
               laterSyntax.AncestorsAndSelf().Contains(enclosingConstruct) &&
               laterSyntax.SpanStart > branchStatement.SpanStart;
    }
}
