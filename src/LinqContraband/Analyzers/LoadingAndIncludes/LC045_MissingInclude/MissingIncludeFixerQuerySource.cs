using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeFixer
{
    /// <summary>
    /// The query expression to wrap with Include: the member-access receiver for reduced
    /// extension syntax (q.ToList()), or the first argument for static syntax
    /// (Enumerable.ToList(q)) - wrapping the type name there would produce invalid code.
    /// </summary>
    private static async Task<ExpressionSyntax?> GetQuerySourceAsync(
        Document document,
        InvocationExpressionSyntax materializer,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel?.GetSymbolInfo(materializer, cancellationToken).Symbol is not IMethodSymbol method)
            return null;

        ExpressionSyntax? source = null;

        if (method.MethodKind == MethodKind.ReducedExtension)
        {
            source = (materializer.Expression as MemberAccessExpressionSyntax)?.Expression;
        }
        else if (method.IsStatic && materializer.ArgumentList.Arguments.Count > 0)
        {
            source = materializer.ArgumentList.Arguments[0].Expression;
        }

        if (source == null)
            return null;

        return semanticModel.GetTypeInfo(source, cancellationToken).Type?.IsIQueryable() == true
            ? source
            : null;
    }
}
