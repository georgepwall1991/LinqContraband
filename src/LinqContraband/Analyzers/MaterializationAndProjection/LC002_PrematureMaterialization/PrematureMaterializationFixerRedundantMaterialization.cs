using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationFixer
{
    private static async Task<Document> RemoveRedundantMaterializationAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (!TryGetInlineMaterializerParts(invocation, out var currentMemberAccess, out var previousInvocation, out var previousMemberAccess))
        {
            return document;
        }

        SyntaxNode replacement;
        var currentMaterializer = currentMemberAccess.Name.Identifier.Text;

        if (currentMaterializer == "AsEnumerable")
        {
            replacement = previousInvocation
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }
        else
        {
            replacement = invocation.WithExpression(
                    currentMemberAccess.WithExpression(previousMemberAccess.Expression.WithoutTrivia()))
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        editor.ReplaceNode(invocation, replacement);
        return editor.GetChangedDocument();
    }
}
