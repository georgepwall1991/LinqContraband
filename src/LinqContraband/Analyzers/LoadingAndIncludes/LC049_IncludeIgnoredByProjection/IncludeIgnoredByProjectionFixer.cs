using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection;

/// <summary>
/// Provides code fixes for LC049. Removes an ignored Include together with the ThenInclude calls chained to it.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IncludeIgnoredByProjectionFixer))]
[Shared]
public sealed class IncludeIgnoredByProjectionFixer : CodeFixProvider
{
    private const string Title = "Remove ignored Include";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(IncludeIgnoredByProjectionAnalyzer.DiagnosticId);

    // Several ignored Includes in one chain are nested inside each other, so a batch of independent text
    // edits would conflict. Fix-all applies every removal through one DocumentEditor instead.
    public sealed override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create((fixAllContext, document, diagnostics) =>
            RemoveIncludesAsync(document, diagnostics, fixAllContext.CancellationToken));

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (TryGetIncludeChain(root, diagnostic, out _) == null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    c => RemoveIncludesAsync(context.Document, ImmutableArray.Create(diagnostic), c)!,
                    nameof(IncludeIgnoredByProjectionFixer)),
                diagnostic);
        }
    }

    private static async Task<Document?> RemoveIncludesAsync(
        Document document,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var handled = new HashSet<InvocationExpressionSyntax>();

        foreach (var diagnostic in diagnostics)
        {
            var include = TryGetIncludeChain(root, diagnostic, out var chainTop);
            if (include == null || chainTop == null || !handled.Add(include)) continue;

            var thenIncludeCount = CountThenIncludes(include, chainTop);
            editor.ReplaceNode(chainTop, (current, _) =>
            {
                // Walk down the (possibly already edited) chain to the Include and keep only its receiver.
                var node = current;
                for (var i = 0; i < thenIncludeCount; i++)
                {
                    if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax thenInclude })
                        return current;
                    node = thenInclude.Expression;
                }

                if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax includeAccess })
                    return current;

                return includeAccess.Expression
                    .WithLeadingTrivia(current.GetLeadingTrivia())
                    .WithTrailingTrivia(current.GetTrailingTrivia());
            });
        }

        return editor.GetChangedDocument();
    }

    private static InvocationExpressionSyntax? TryGetIncludeChain(
        SyntaxNode root,
        Diagnostic diagnostic,
        out InvocationExpressionSyntax? chainTop)
    {
        chainTop = null;
        if (!diagnostic.Properties.ContainsKey(IncludeIgnoredByProjectionAnalyzer.FluentPropertyName)) return null;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node is not SimpleNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } name ||
            memberAccess.Name != name ||
            memberAccess.Parent is not InvocationExpressionSyntax include ||
            include.Expression != memberAccess)
        {
            return null;
        }

        chainTop = include;
        while (chainTop.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ThenInclude" } next &&
               next.Expression == chainTop &&
               next.Parent is InvocationExpressionSyntax nextInvocation)
        {
            chainTop = nextInvocation;
        }

        return include;
    }

    private static int CountThenIncludes(InvocationExpressionSyntax include, InvocationExpressionSyntax chainTop)
    {
        var count = 0;
        for (var current = chainTop; current != include; count++)
        {
            current = (InvocationExpressionSyntax)((MemberAccessExpressionSyntax)current.Expression).Expression;
        }

        return count;
    }
}
