using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesFixer
{
    private static async Task<Document> ApplyFixAsync(
        Document document,
        FieldDeclarationSyntax fieldDeclaration,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null ||
            !UseFrozenSetForStaticMembershipCachesAnalysis.TryGetFrozenSetSupport(semanticModel.Compilation, out var support))
        {
            return document;
        }

        if (!TryCreateFixPlan(fieldDeclaration, semanticModel, support, cancellationToken, out var plan))
            return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(fieldDeclaration, plan.RewrittenFieldDeclaration);
        editor.EnsureUsing("System.Collections.Frozen");

        var changedDocument = editor.GetChangedDocument();
        var simplifiedDocument = await Simplifier.ReduceAsync(
            changedDocument,
            Simplifier.Annotation,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await Formatter.FormatAsync(
            simplifiedDocument,
            Formatter.Annotation,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private readonly struct FixPlan
    {
        public FixPlan(FieldDeclarationSyntax rewrittenFieldDeclaration)
        {
            RewrittenFieldDeclaration = rewrittenFieldDeclaration;
        }

        public FieldDeclarationSyntax RewrittenFieldDeclaration { get; }
    }
}
