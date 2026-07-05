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

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MissingIncludeAnalyzer.NavigationPathProperty, out var navigationPath) ||
                string.IsNullOrWhiteSpace(navigationPath))
            {
                continue;
            }

            if (diagnostic.AdditionalLocations.Count == 0)
                continue;

            var materializerNode = root?.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true);
            var materializer = materializerNode as InvocationExpressionSyntax ??
                               materializerNode?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (materializer == null)
                continue;

            if (await GetQuerySourceAsync(context.Document, materializer, context.CancellationToken).ConfigureAwait(false) == null)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add .Include() for '{navigationPath}'",
                    c => ApplyFixAsync(context.Document, materializer, navigationPath!, c),
                    "LC045_AddInclude:" + navigationPath),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax materializer,
        string navigationPath,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var source = await GetQuerySourceAsync(document, materializer, cancellationToken).ConfigureAwait(false);
        if (source == null)
            return document;

        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        ExpressionSyntax current = source;
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

        editor.ReplaceNode(source, current.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static string EscapeIdentifier(string name)
    {
        // A navigation named after a reserved keyword (e.g. `@event`) is stored unescaped in
        // the diagnostic path; emit it back with the verbatim prefix or the fix won't compile.
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    /// <summary>
    /// The query expression to wrap with Include: the member-access receiver for reduced
    /// extension syntax (q.ToList()), or the first argument for static syntax
    /// (Enumerable.ToList(q)) — wrapping the type name there would produce invalid code.
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
