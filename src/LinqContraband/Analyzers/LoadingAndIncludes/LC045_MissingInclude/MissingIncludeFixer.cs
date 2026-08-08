using System;
using System.Collections.Generic;
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

        // Restating a prefix the query already Includes leaves the user with a redundant chain
        // (`.Include(o => o.Customer).Include(x => x.Customer).ThenInclude(...)`). When an
        // existing lambda Include already covers a prefix, extend that chain instead.
        if (TryExtendExistingInclude(editor, source, navigationPath))
            return editor.GetChangedDocument();

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


    /// <summary>
    /// Appends the missing suffix as <c>ThenInclude</c> to the longest existing lambda
    /// <c>Include</c>/<c>ThenInclude</c> chain that already covers a prefix of the flagged path.
    /// Returns false when no chain qualifies, leaving the caller to wrap the query source.
    /// </summary>
    private static bool TryExtendExistingInclude(
        DocumentEditor editor,
        SyntaxNode source,
        string navigationPath)
    {
        var segments = navigationPath.Split('.');

        // The analyzer hands over the receiver of the materializer, so the existing operators
        // are inside that expression. Walk down the receiver chain, then read it in source order.
        var chain = new List<InvocationExpressionSyntax>();
        for (var node = source as ExpressionSyntax; node != null; )
        {
            if (node is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            chain.Add(invocation);
            node = memberAccess.Expression;
        }

        chain.Reverse();

        InvocationExpressionSyntax? bestChain = null;
        var bestDepth = 0;
        var accumulated = new List<string>();

        foreach (var invocation in chain)
        {
            var name = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;
            if (name is not ("Include" or "ThenInclude"))
                continue;

            if (!TryGetLambdaPathSegments(invocation, out var pathSegments))
            {
                // A string Include returns IQueryable, so nothing can be appended to it, and an
                // unparsed shape must not be guessed at.
                accumulated.Clear();
                continue;
            }

            if (name == "Include")
                accumulated.Clear();

            accumulated.AddRange(pathSegments);

            if (accumulated.Count < segments.Length && IsPrefix(accumulated, segments))
            {
                bestChain = invocation;
                bestDepth = accumulated.Count;
            }
        }

        if (bestChain == null)
            return false;

        ExpressionSyntax extended = bestChain;
        for (var index = bestDepth; index < segments.Length; index++)
        {
            extended = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    extended,
                    SyntaxFactory.IdentifierName("ThenInclude")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.ParseExpression($"x => x.{EscapeIdentifier(segments[index])}")))));
        }

        editor.ReplaceNode(bestChain, extended);
        return true;
    }

    private static bool IsPrefix(List<string> candidate, string[] path)
    {
        if (candidate.Count == 0 || candidate.Count > path.Length)
            return false;

        for (var index = 0; index < candidate.Count; index++)
        {
            if (!string.Equals(candidate[index], path[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The navigation segments named by a single-argument lambda Include/ThenInclude, or false
    /// for any other shape (string overloads, filtered lambdas, casts, method calls).
    /// </summary>
    private static bool TryGetLambdaPathSegments(
        InvocationExpressionSyntax invocation,
        out List<string> segments)
    {
        segments = new List<string>();
        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;

        if (invocation.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda ||
            lambda.Body is not ExpressionSyntax body)
        {
            return false;
        }

        var parameterName = lambda.Parameter.Identifier.ValueText;
        var names = new List<string>();
        while (body is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            names.Insert(0, memberAccess.Name.Identifier.ValueText);
            body = memberAccess.Expression;
        }

        if (names.Count == 0 ||
            body is not IdentifierNameSyntax identifier ||
            identifier.Identifier.ValueText != parameterName)
        {
            return false;
        }

        segments = names;
        return true;
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
