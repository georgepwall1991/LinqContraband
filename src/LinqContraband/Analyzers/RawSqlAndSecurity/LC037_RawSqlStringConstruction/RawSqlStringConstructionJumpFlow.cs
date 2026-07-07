using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool HasPotentiallySkippingAncestor(SyntaxNode node, int referenceStart)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                return false;

            if ((IsLoopSyntax(ancestor) || ancestor is SwitchStatementSyntax or SwitchSectionSyntax) &&
                ContainsPosition(ancestor, referenceStart))
            {
                continue;
            }

            if (IsLoopSyntax(ancestor) || ancestor is
                SwitchStatementSyntax or SwitchSectionSyntax or ConditionalExpressionSyntax or
                TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPosition(SyntaxNode container, int position)
    {
        return container.SpanStart <= position && container.Span.End >= position;
    }

    private static bool JumpSkipsReference(SyntaxNode jumpStatement, SyntaxNode survivingNode, int referenceStart, bool allowSwitch)
    {
        if (jumpStatement.SpanStart >= referenceStart)
            return false;

        foreach (var ancestor in jumpStatement.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                return false;

            if (IsLoopSyntax(ancestor) || (allowSwitch && ancestor is SwitchStatementSyntax))
                return ContainsNode(ancestor, survivingNode);
        }

        return false;
    }
}
