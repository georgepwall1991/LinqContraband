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

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Provides code fixes for LC010. Moves SaveChanges/SaveChangesAsync calls to after the enclosing loop.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SaveChangesInLoopFixer))]
[Shared]
public class SaveChangesInLoopFixer : CodeFixProvider
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

        // Find the expression statement containing SaveChanges
        var expressionStatement = invocation.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault();
        if (expressionStatement == null)
        {
            // May be an await expression: await db.SaveChangesAsync()
            var awaitExpr = invocation.AncestorsAndSelf().OfType<AwaitExpressionSyntax>().FirstOrDefault();
            expressionStatement = awaitExpr?.Parent as ExpressionStatementSyntax;
        }

        if (expressionStatement == null) return document;

        // Find the enclosing loop
        var loop = expressionStatement.Ancestors().FirstOrDefault(n =>
            n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax);

        if (loop == null) return document;

        // Create the new statement to insert after the loop (preserve the full statement including await if present)
        var newStatement = expressionStatement
            .WithLeadingTrivia(loop.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        // Remove the statement from inside the loop
        editor.RemoveNode(expressionStatement);

        // Insert after the loop
        editor.InsertAfter(loop, newStatement);

        return editor.GetChangedDocument();
    }
}
