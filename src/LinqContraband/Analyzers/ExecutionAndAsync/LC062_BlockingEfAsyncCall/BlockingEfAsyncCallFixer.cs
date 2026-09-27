using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Analyzers.LC008_SyncBlocker;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Provides code fixes for LC062. In an async method, lambda or local function the blocking access becomes
/// <c>await</c> on the task.
/// </summary>
/// <remarks>
/// There is no synchronous rewrite (<c>ToListAsync().Result</c> to <c>ToList()</c>): async-only interceptors
/// (<c>SaveChangesInterceptor</c>, <c>DbCommandInterceptor</c>), async-only overrides and the evaluation of dropped
/// arguments make it impossible to prove the synchronous call equivalent. The await fix is withheld where
/// <c>await</c> is not allowed or would strand a ref-like value, inside the <c>try</c> block of a statement with a
/// catch clause (awaiting unwraps the <c>AggregateException</c> and changes which handler runs), and when a directive
/// sits inside the replaced expression. The fixer compiles the rewritten document and only offers a rewrite that adds
/// no errors and keeps the value's type.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingEfAsyncCallFixer))]
[Shared]
public sealed class BlockingEfAsyncCallFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingEfAsyncCallAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var site = FindSite(root, diagnostic.Location.SourceSpan);
            if (site == null || !TryGetTaskExpression(site, out var taskExpression, out var producesValue)) continue;
            if (IsInsideTryWithCatch(site)) continue;
            // The rewrite does not keep a #pragma, #if or other directive inside the replaced expression.
            if (ContainsDirective(site)) continue;

            // Only in async code, where await is allowed and strands no ref-like value.
            if (SyncBlockerFixer.IsInvalidAwaitContext(site) ||
                SyncBlockerFixer.WouldStrandRefStructLocal(site, semanticModel, cancellationToken) ||
                WouldStrandRefLikeValue(site, semanticModel, cancellationToken))
            {
                continue;
            }

            var originalType = producesValue ? semanticModel.GetTypeInfo(site, cancellationToken).Type : null;
            errorsBefore ??= CountErrors(semanticModel, cancellationToken);

            var marker = new SyntaxAnnotation();
            var draft = await CreateAwaitFixAsync(document, site, taskExpression, marker, cancellationToken).ConfigureAwait(false);
            if (!await IsAcceptableAsync(draft, marker, errorsBefore.Value, originalType, cancellationToken).ConfigureAwait(false))
                continue;

            var awaited = await WithoutMarkerAsync(draft, marker, cancellationToken).ConfigureAwait(false);
            context.RegisterCodeFix(
                CodeAction.Create("Await the task instead of blocking", _ => Task.FromResult(awaited), nameof(BlockingEfAsyncCallFixer)),
                diagnostic);
        }
    }

    /// <summary>
    /// <c>.Result</c> and <c>.Wait()</c> wrap failures in <c>AggregateException</c>; <c>await</c> does not. True when
    /// the site is in the <c>try</c> block of a statement, in the same member, with any catch clause, whose handler
    /// selection the rewrite could change.
    /// </summary>
    private static bool IsInsideTryWithCatch(SyntaxNode site)
    {
        foreach (var ancestor in site.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax)
                break;

            if (ancestor is TryStatementSyntax { Catches.Count: > 0 } tryStatement && tryStatement.Block.Span.Contains(site.Span))
                return true;
        }

        return false;
    }

    private static bool ContainsDirective(ExpressionSyntax site)
    {
        return site.DescendantTrivia(site.Span).Any(trivia =>
            trivia.HasStructure || trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia));
    }

    private static ExpressionSyntax? FindSite(SyntaxNode root, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        return node.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault(expression => expression.Span == span);
    }

    /// <summary>
    /// The task a blocking site waits on: <c>X</c> in <c>X.Result</c>, <c>X.Wait()</c> and
    /// <c>X.GetAwaiter().GetResult()</c>. <paramref name="producesValue"/> is false for <c>Wait()</c>, which is void.
    /// </summary>
    private static bool TryGetTaskExpression(ExpressionSyntax site, out ExpressionSyntax taskExpression, out bool producesValue)
    {
        taskExpression = null!;
        producesValue = true;

        switch (site)
        {
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } result:
                taskExpression = result.Expression;
                return true;

            case InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Wait" } wait
            }:
                taskExpression = wait.Expression;
                producesValue = false;
                return true;

            case InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "GetResult",
                    Expression: InvocationExpressionSyntax
                    {
                        ArgumentList.Arguments.Count: 0,
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } getAwaiter
                    }
                }
            }:
                taskExpression = getAwaiter.Expression;
                return true;

            default:
                return false;
        }
    }

    private static async Task<Document> CreateAwaitFixAsync(
        Document document,
        ExpressionSyntax site,
        ExpressionSyntax taskExpression,
        SyntaxAnnotation marker,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(taskExpression.WithoutTrivia());
        if (NeedsParentheses(site))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        editor.ReplaceNode(
            site,
            replacement
                .WithLeadingTrivia(site.GetLeadingTrivia())
                .WithTrailingTrivia(KeepComments(site, taskExpression).AddRange(site.GetTrailingTrivia()))
                .WithAdditionalAnnotations(Formatter.Annotation, marker));

        return await FormatAsync(editor.GetChangedDocument(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the site is in a ref struct, or when a ref-like parameter or local, or a <c>ref</c> or
    /// <c>ref readonly</c> local, declared before the site is read after it: an inserted <c>await</c> would keep it
    /// live across the suspension point, which the async rewriter rejects (CS4007, CS4012, CS9217). LC008's helper
    /// covers ref-like locals; this adds the rest.
    /// </summary>
    private static bool WouldStrandRefLikeValue(SyntaxNode site, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // Inside a ref struct, `this` can be read implicitly (a bare field or method name), so any member of one is
        // treated as keeping a ref-like value live.
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken)?.ContainingType is { IsRefLikeType: true })
            return true;

        var body = site.Ancestors().FirstOrDefault(node =>
            node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);
        if (body == null)
            return false;

        foreach (var node in body.DescendantNodes())
        {
            if (node.SpanStart <= site.Span.End)
                continue;

            if (node is not IdentifierNameSyntax identifier)
                continue;

            switch (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol)
            {
                case IParameterSymbol { Type.IsRefLikeType: true }:
                    return true;
                // A ref-like local, or a ref / ref readonly local, cannot be live across an await (CS4007, CS9217).
                case ILocalSymbol local
                    when (local.Type.IsRefLikeType || local.RefKind != RefKind.None) &&
                         local.DeclaringSyntaxReferences.Any(reference => reference.Span.Start < site.SpanStart):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The comments the rewrite would drop: those after <paramref name="kept"/> inside <paramref name="site"/>, as in
    /// <c>db.Users.ToListAsync() /* why */ .Result</c>. They move after the new expression, each followed by a line
    /// break when it is a single-line comment, so it cannot swallow the rest of the line.
    /// </summary>
    private static SyntaxTriviaList KeepComments(ExpressionSyntax site, SyntaxNode kept)
    {
        var comments = new System.Collections.Generic.List<SyntaxTrivia>();
        var lastToken = site.GetLastToken();

        void Add(SyntaxTriviaList trivia)
        {
            foreach (var item in trivia)
            {
                if (!item.IsKind(SyntaxKind.SingleLineCommentTrivia) && !item.IsKind(SyntaxKind.MultiLineCommentTrivia))
                    continue;

                comments.Add(SyntaxFactory.Space);
                comments.Add(item);
                if (item.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    comments.Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
            }
        }

        Add(kept.GetLastToken().TrailingTrivia);
        foreach (var token in site.DescendantTokens())
        {
            if (token.SpanStart < kept.Span.End)
                continue;

            Add(token.LeadingTrivia);
            if (token != lastToken)
                Add(token.TrailingTrivia);
        }

        return SyntaxFactory.TriviaList(comments);
    }

    private static bool NeedsParentheses(ExpressionSyntax site)
    {
        return site.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == site,
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression == site,
            InvocationExpressionSyntax invocation => invocation.Expression == site,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression == site,
            PostfixUnaryExpressionSyntax => true,
            _ => false
        };
    }

    private static Task<Document> FormatAsync(Document document, CancellationToken cancellationToken)
    {
        return Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken);
    }

    private static async Task<bool> IsAcceptableAsync(
        Document fixedDocument,
        SyntaxAnnotation marker,
        int errorsBefore,
        ITypeSymbol? originalType,
        CancellationToken cancellationToken)
    {
        var newModel = await fixedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = await fixedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (newModel == null || newRoot == null || CountErrors(newModel, cancellationToken) > errorsBefore)
            return false;

        if (originalType == null)
            return true;

        var replaced = newRoot.GetAnnotatedNodes(marker).OfType<ExpressionSyntax>().FirstOrDefault();
        var newType = replaced == null ? null : newModel.GetTypeInfo(replaced, cancellationToken).Type;

        // Types from two compilations are compared by their display, which includes type arguments and nullability.
        return newType != null &&
               newType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               originalType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static async Task<Document> WithoutMarkerAsync(Document document, SyntaxAnnotation marker, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var marked = root?.GetAnnotatedNodes(marker).FirstOrDefault();
        return root == null || marked == null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(marked, marked.WithoutAnnotations(marker)));
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
