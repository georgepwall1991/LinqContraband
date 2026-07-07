using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateAnalyzer
{
    private readonly struct LocalOrigin
    {
        public LocalOrigin(int position, bool isNoTracking, SyntaxNode? syntax)
        {
            Position = position;
            IsNoTracking = isNoTracking;
            Syntax = syntax;
        }

        public int Position { get; }
        public bool IsNoTracking { get; }
        public SyntaxNode? Syntax { get; }
    }

    private static bool IsConditionalRelativeTo(SyntaxNode? originSyntax, int usePosition, SyntaxNode rootSyntax)
    {
        if (originSyntax == null) return false;

        for (var node = originSyntax.Parent; node != null && node != rootSyntax; node = node.Parent)
        {
            var isBranching = node is IfStatementSyntax
                or SwitchStatementSyntax
                or SwitchExpressionSyntax
                or ConditionalExpressionSyntax
                or CatchClauseSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or CommonForEachStatementSyntax;

            if (isBranching && !node.Span.Contains(usePosition))
                return true;
        }

        return false;
    }
}
