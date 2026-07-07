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
using LinqContraband.Extensions;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Provides code fixes for LC010. Moves SaveChanges/SaveChangesAsync calls to after the enclosing loop.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SaveChangesInLoopFixer))]
[Shared]
public sealed partial class SaveChangesInLoopFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SaveChangesInLoopAnalyzer.DiagnosticId);

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
        if (!TryGetMovableSaveStatement(invocation, out _, out var loop)) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null ||
            IsSaveReceiverDeclaredInsideLoop(invocation, loop, semanticModel, context.CancellationToken))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Move SaveChanges after loop",
                c => ApplyFixAsync(context.Document, invocation, c),
                "MoveSaveChangesAfterLoop"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (!TryGetMovableSaveStatement(invocation, out var expressionStatement, out var loop)) return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null ||
            IsSaveReceiverDeclaredInsideLoop(invocation, loop, semanticModel, cancellationToken))
        {
            return document;
        }

        // Create the new statement to insert after the loop (preserve the full statement including await if present).
        // Terminate it with the document's own line ending so a CRLF file does not gain a lone LF line.
        var newStatement = expressionStatement
            .WithLeadingTrivia(loop.GetLeadingTrivia())
            .WithTrailingTrivia(loop.GetDocumentEndOfLine());

        // Remove the statement from inside the loop
        editor.RemoveNode(expressionStatement);

        // Insert after the loop
        editor.InsertAfter(loop, newStatement);

        return editor.GetChangedDocument();
    }
}
