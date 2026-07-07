using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

/// <summary>
/// Provides code fixes for LC012. Replaces RemoveRange with ExecuteDelete.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OptimizeRemoveRangeFixer))]
[Shared]
public sealed partial class OptimizeRemoveRangeFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OptimizeRemoveRangeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        if (!await CanSafelyRewriteAsync(context.Document, invocation, context.CancellationToken).ConfigureAwait(false))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteDelete()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseExecuteDelete"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // RemoveRange(query) -> query.ExecuteDelete() / await query.ExecuteDeleteAsync()
        if (invocation.ArgumentList.Arguments.Count == 0)
            return editor.GetChangedDocument();

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var mode = semanticModel == null ? RewriteMode.Sync : DetermineRewriteMode(invocation, semanticModel);

        // CanSafelyRewriteAsync already declined registration for RewriteMode.None, so a
        // safe sync/async target always exists by the time the fix is applied.
        if (mode == RewriteMode.None)
            return document;

        var queryExpression = invocation.ArgumentList.Arguments[0].Expression;

        var executeDeleteName = SyntaxFactory.IdentifierName(mode == RewriteMode.Async ? "ExecuteDeleteAsync" : "ExecuteDelete");
        var memberAccess = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, queryExpression, executeDeleteName);
        ExpressionSyntax replacement = SyntaxFactory.InvocationExpression(memberAccess);

        // Inside an async context the synchronous ExecuteDelete() rewrite would inject a
        // blocking, sync-over-async database call (the smell LC008 flags), so await the
        // async overload instead.
        if (mode == RewriteMode.Async)
            replacement = SyntaxFactory.AwaitExpression(replacement);

        // Add warning comment
        var warningComment = SyntaxFactory.Comment("// Warning: ExecuteDelete bypasses change tracking and cascades.");
        var replacementWithComment = replacement.WithLeadingTrivia(invocation.GetLeadingTrivia().Add(warningComment).Add(SyntaxFactory.ElasticLineFeed));

        editor.ReplaceNode(invocation, replacementWithComment);

        return editor.GetChangedDocument();
    }

    private enum RewriteMode
    {
        None,
        Sync,
        Async
    }
}
