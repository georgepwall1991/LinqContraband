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

namespace LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters;

/// <summary>
/// Provides code fixes for LC021. Removes IgnoreQueryFilters() calls from query chains.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidIgnoreQueryFiltersFixer))]
[Shared]
public class AvoidIgnoreQueryFiltersFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidIgnoreQueryFiltersAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var invocation = node as InvocationExpressionSyntax
                         ?? node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove IgnoreQueryFilters()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "RemoveIgnoreQueryFilters"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var receiver = memberAccess.Expression;

        editor.ReplaceNode(invocation, receiver
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia()));

        return editor.GetChangedDocument();
    }
}
