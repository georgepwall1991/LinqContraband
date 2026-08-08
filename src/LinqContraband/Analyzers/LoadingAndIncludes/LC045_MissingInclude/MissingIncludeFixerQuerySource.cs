using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeFixer
{
    /// <summary>
    /// Revalidates the analyzer-provided query source before a rewrite. The additional location
    /// is normalized to this exact expression for both materializer and direct-foreach findings.
    /// </summary>
    private static async Task<ExpressionSyntax?> GetQueryableSourceAsync(
        Document document,
        ExpressionSyntax querySource,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel?.GetTypeInfo(querySource, cancellationToken).Type?.IsIQueryable() == true)
            return querySource;

        return semanticModel == null
            ? null
            : TryGetWidenedLocalQueryable(querySource, semanticModel, cancellationToken);
    }

    /// <summary>
    /// A query widened to <c>IEnumerable&lt;T&gt;</c> — <c>IEnumerable&lt;Order&gt; source =
    /// db.Orders;</c> — cannot take <c>Include</c> at the point it is consumed, because the
    /// extension is declared on <c>IQueryable&lt;T&gt;</c>. It can take it where the local was
    /// given the query, so the rewrite is redirected onto that initializer:
    /// <c>IEnumerable&lt;Order&gt; source = db.Orders.Include(x =&gt; x.Customer);</c> still
    /// converts to the declared type, because <c>Include</c> returns an
    /// <c>IIncludableQueryable&lt;T, P&gt;</c>.
    /// The local must be declared with an initializer, never reassigned, and given something that
    /// is itself queryable, so that the expression being wrapped really is the one the consumed
    /// value came from and can actually take <c>Include</c>. Neither of those two requirements is
    /// reachable today — the analyzer declines a reassigned source outright, and when the local
    /// was given an already-materialized collection the query source it reports is the original
    /// materializer rather than this local — so they are forward guards, each covered by a test
    /// pinning the premise that keeps them unreachable.
    /// </summary>
    private static ExpressionSyntax? TryGetWidenedLocalQueryable(
        ExpressionSyntax querySource,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (querySource is not IdentifierNameSyntax identifier)
            return null;

        if (
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
            is not ILocalSymbol local
        )
        {
            return null;
        }

        var declarator = local
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .SingleOrDefault();
        if (declarator?.Initializer?.Value is not { } initializer)
            return null;

        var body = declarator.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is { } method
            ? (SyntaxNode?)method
            : declarator.FirstAncestorOrSelf<LocalFunctionStatementSyntax>();
        if (body == null || IsReassigned(body, local, semanticModel, cancellationToken))
            return null;

        return semanticModel.GetTypeInfo(initializer, cancellationToken).Type?.IsIQueryable()
            == true
            ? initializer
            : null;
    }

    private static bool IsReassigned(
        SyntaxNode body,
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (
                assignment.Left is IdentifierNameSyntax target
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(target, cancellationToken).Symbol,
                    local
                )
            )
            {
                return true;
            }
        }

        return false;
    }
}
