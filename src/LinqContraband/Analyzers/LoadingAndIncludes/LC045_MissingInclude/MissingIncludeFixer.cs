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

        editor.ReplaceNode(source, current.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(trailingTrivia));

        return editor.GetChangedDocument();
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
