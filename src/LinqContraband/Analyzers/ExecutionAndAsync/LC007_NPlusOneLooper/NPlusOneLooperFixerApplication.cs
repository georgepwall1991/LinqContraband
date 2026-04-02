using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

public sealed partial class NPlusOneLooperFixer
{
    private static async Task<Document> ApplyExplicitLoadFixAsync(
        Document document,
        SyntaxNode root,
        ExplicitLoadFixContext fixContext,
        CancellationToken cancellationToken)
    {
        var currentLoadStatement = root.FindNode(fixContext.LoadStatement.Span) as ExpressionStatementSyntax
                                   ?? root.FindToken(fixContext.LoadStatement.Span.Start).Parent?.AncestorsAndSelf()
                                       .OfType<ExpressionStatementSyntax>()
                                       .FirstOrDefault();
        if (currentLoadStatement == null)
            return document;

        var removedLoadRoot = root.RemoveNode(currentLoadStatement, SyntaxRemoveOptions.KeepNoTrivia);
        if (removedLoadRoot == null)
            return document;

        var currentQueryTarget = removedLoadRoot.FindNode(fixContext.QueryTargetNode.Span) as ExpressionSyntax;
        if (currentQueryTarget == null)
            return document;

        var updatedRoot = removedLoadRoot.ReplaceNode(currentQueryTarget, fixContext.RewrittenQuerySource);
        var updatedDocument = document.WithSyntaxRoot(updatedRoot);
        var editor = await DocumentEditor.CreateAsync(updatedDocument, cancellationToken).ConfigureAwait(false);
        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        return editor.GetChangedDocument();
    }
}
