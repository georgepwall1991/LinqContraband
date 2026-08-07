using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

/// <summary>
/// Provides code fixes for LC045. Inserts .Include(x => x.Nav) (and .ThenInclude for nested
/// paths) immediately before a materializer or direct foreach source so the accessed navigation
/// is eagerly loaded.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingIncludeFixer))]
[Shared]
public sealed partial class MissingIncludeFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingIncludeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MissingIncludeAnalyzer.NavigationPathProperty, out var navigationPath) ||
                string.IsNullOrWhiteSpace(navigationPath))
            {
                continue;
            }

            if (diagnostic.AdditionalLocations.Count == 0)
                continue;

            var querySourceNode = root?.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true);
            var querySource = querySourceNode as ExpressionSyntax ??
                              querySourceNode?.FirstAncestorOrSelf<ExpressionSyntax>();
            if (querySource == null)
                continue;

            if (await GetQueryableSourceAsync(context.Document, querySource, context.CancellationToken).ConfigureAwait(false) == null)
                continue;

            // Wrapping the source of an `await foreach` turns it into an IQueryable that is no
            // longer an IAsyncEnumerable (CS8415), so the rewrite has to restore the async
            // bridge. Without the bridge in the compilation there is no compiling fix to offer.
            if (IsDirectAsyncForEachSource(querySource) &&
                !await HasAsyncEnumerableBridgeAsync(context.Document, context.CancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add .Include() for '{navigationPath}'",
                    c => ApplyFixAsync(context.Document, querySource, navigationPath!, c),
                    "LC045_AddInclude:" + navigationPath),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        ExpressionSyntax querySource,
        string navigationPath,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var source = await GetQueryableSourceAsync(document, querySource, cancellationToken).ConfigureAwait(false);
        if (source == null)
            return document;

        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        var leadingTrivia = source.GetLeadingTrivia();
        var trailingTrivia = source.GetTrailingTrivia();
        ExpressionSyntax current = ParenthesizeForMemberAccess((ExpressionSyntax)source.WithoutTrivia());
        var first = true;

        foreach (var segment in navigationPath.Split('.'))
        {
            var methodName = first ? "Include" : "ThenInclude";
            var lambda = SyntaxFactory.ParseExpression($"x => x.{EscapeIdentifier(segment)}");
            current = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    current,
                    SyntaxFactory.IdentifierName(methodName)),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))));
            first = false;
        }

        if (IsDirectAsyncForEachSource(source))
        {
            current = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    current,
                    SyntaxFactory.IdentifierName("AsAsyncEnumerable")),
                SyntaxFactory.ArgumentList());
        }

        editor.ReplaceNode(source, current.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(trailingTrivia));

        return editor.GetChangedDocument();
    }

    /// <summary>
    /// True when the expression is enumerated directly by an <c>await foreach</c>. Only a
    /// <c>DbSet&lt;T&gt;</c>-shaped source reaches this state: it is both an
    /// <c>IQueryable&lt;T&gt;</c> and an <c>IAsyncEnumerable&lt;T&gt;</c>, and <c>Include</c>
    /// preserves only the first.
    /// </summary>
    private static bool IsDirectAsyncForEachSource(ExpressionSyntax source)
    {
        return source.Parent is CommonForEachStatementSyntax forEachStatement
            && forEachStatement.Expression == source
            && !forEachStatement.AwaitKeyword.IsKind(SyntaxKind.None);
    }

    private static async Task<bool> HasAsyncEnumerableBridgeAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var extensions = semanticModel?.Compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        return extensions?.GetMembers("AsAsyncEnumerable").Length > 0;
    }

    private static string EscapeIdentifier(string name)
    {
        // A navigation named after a reserved keyword (e.g. `@event`) is stored unescaped in
        // the diagnostic path; emit it back with the verbatim prefix or the fix won't compile.
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    private static ExpressionSyntax ParenthesizeForMemberAccess(ExpressionSyntax expression)
    {
        return expression is CastExpressionSyntax || expression.IsKind(SyntaxKind.AsExpression)
            ? SyntaxFactory.ParenthesizedExpression(expression)
            : expression;
    }

}
