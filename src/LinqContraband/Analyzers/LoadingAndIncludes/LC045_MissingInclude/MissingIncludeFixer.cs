using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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
/// paths) immediately before the materializer so the accessed navigation is eagerly loaded.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingIncludeFixer))]
[Shared]
public sealed class MissingIncludeFixer : CodeFixProvider
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
        var diagnostic = context.Diagnostics.First();

        if (!diagnostic.Properties.TryGetValue(MissingIncludeAnalyzer.NavigationPathProperty, out var navigationPath) ||
            string.IsNullOrWhiteSpace(navigationPath))
        {
            return;
        }

        if (diagnostic.AdditionalLocations.Count == 0)
            return;

        var materializerNode = root?.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true);
        var materializer = materializerNode as InvocationExpressionSyntax ??
                           materializerNode?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (materializer?.Expression is not MemberAccessExpressionSyntax)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add .Include() for '{navigationPath}'",
                c => ApplyFixAsync(context.Document, materializer, navigationPath!, c),
                "LC045_AddInclude:" + navigationPath),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax materializer,
        string navigationPath,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        if (materializer.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        // Wrap the materializer's receiver: recv.ToList() => recv.Include(x => x.Nav).ToList().
        // This is always a valid position for Include and lands after any existing Includes.
        var source = memberAccess.Expression;
        ExpressionSyntax current = source;
        var first = true;

        foreach (var segment in navigationPath.Split('.'))
        {
            var methodName = first ? "Include" : "ThenInclude";
            var lambda = SyntaxFactory.ParseExpression($"x => x.{segment}");
            current = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    current,
                    SyntaxFactory.IdentifierName(methodName)),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))));
            first = false;
        }

        editor.ReplaceNode(source, current.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }
}
