using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

public sealed partial class SyncBlockerFixer
{
    private static bool IsInvalidAwaitContext(SyntaxNode invocation)
    {
        for (SyntaxNode? node = invocation; node != null; node = node.Parent)
        {
            var parent = node.Parent;
            if (parent == null) continue;

            switch (parent)
            {
                case FromClauseSyntax fromClause when fromClause.Expression == node:
                    // Await is only legal in the first from clause of a query expression.
                    return fromClause.Parent is not QueryExpressionSyntax;

                case JoinClauseSyntax joinClause when joinClause.InExpression == node:
                    return false;

                case QueryClauseSyntax:
                case SelectOrGroupClauseSyntax:
                case OrderingSyntax:
                case QueryContinuationSyntax:
                    return true;
            }

            if (parent is AnonymousFunctionExpressionSyntax anonymousFunction)
                return !anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);

            if (parent is LocalFunctionStatementSyntax localFunction)
                return !localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);

            if (parent is BaseMethodDeclarationSyntax method)
                return !method.Modifiers.Any(SyntaxKind.AsyncKeyword);
        }

        return false;
    }
}
