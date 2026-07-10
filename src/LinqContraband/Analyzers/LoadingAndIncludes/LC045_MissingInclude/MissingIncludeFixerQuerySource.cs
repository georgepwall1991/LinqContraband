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
        return
            semanticModel?.GetTypeInfo(querySource, cancellationToken).Type?.IsIQueryable() == true
            ? querySource
            : null;
    }
}
