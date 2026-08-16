using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

public sealed partial class SyncBlockerFixer
{
    /// <summary>
    /// Reports whether inserting an await at <paramref name="invocation"/> would leave a
    /// ref struct local (Span&lt;T&gt;, ReadOnlySpan&lt;T&gt;, any ref struct) live across the
    /// suspension point. That produces CS4007, which binding does not report — it is raised by
    /// the async rewriter during emit — so the rewrite looks valid right up until the build.
    /// </summary>
    private static bool WouldStrandRefStructLocal(
        SyntaxNode invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var body = FindEnclosingBody(invocation);
        if (body == null)
            return false;

        foreach (var declaration in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.SpanStart >= invocation.SpanStart)
                continue;

            if (
                semanticModel.GetDeclaredSymbol(declaration, cancellationToken)
                    is not ILocalSymbol local
                || local.Type is not { IsRefLikeType: true }
            )
            {
                continue;
            }

            foreach (var reference in body.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (reference.SpanStart <= invocation.Span.End)
                    continue;

                if (reference.Identifier.ValueText != local.Name)
                    continue;

                if (
                    SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol,
                        local
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SyntaxNode? FindEnclosingBody(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current != null; current = current.Parent)
        {
            switch (current)
            {
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return (SyntaxNode?)anonymousFunction.Block ?? anonymousFunction.ExpressionBody;
                case LocalFunctionStatementSyntax localFunction:
                    return (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody;
                case BaseMethodDeclarationSyntax method:
                    return (SyntaxNode?)method.Body ?? method.ExpressionBody;
            }
        }

        return null;
    }
}
