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

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

/// <summary>
/// Provides code fixes for LC003. Replaces Count() comparisons with Any() for more efficient existence checks.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AnyOverCountFixer))]
[Shared]
public class AnyOverCountFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AnyOverCountAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the binary expression identified by the diagnostic.
        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var binaryExpr = token.Parent.AncestorsAndSelf().OfType<BinaryExpressionSyntax>()
            .FirstOrDefault();

        if (binaryExpr == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace with Any()",
                c => ApplyFixAsync(context.Document, binaryExpr, c),
                "ReplaceCountWithAny"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, BinaryExpressionSyntax binaryExpr,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // Identify the invocation of Count/LongCount
        InvocationExpressionSyntax? countInvocation = null;

        if (binaryExpr.Left is InvocationExpressionSyntax leftInv)
            // Check if it is the Count call (simplistic check, relying on analyzer to catch right nodes)
            countInvocation = leftInv;
        else if (binaryExpr.Right is InvocationExpressionSyntax rightInv) countInvocation = rightInv;

        // If we have casts, unwrap them
        if (countInvocation == null)
        {
            var potentialLeft = binaryExpr.Left;
            while (potentialLeft is CastExpressionSyntax cast) potentialLeft = cast.Expression;
            if (potentialLeft is InvocationExpressionSyntax l) countInvocation = l;

            var potentialRight = binaryExpr.Right;
            while (potentialRight is CastExpressionSyntax cast) potentialRight = cast.Expression;
            if (potentialRight is InvocationExpressionSyntax r) countInvocation = r;
        }

        if (countInvocation == null) return document;

        // Get the member access to find 'query' (the expression on which Count is called)
        if (countInvocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var queryExpression = memberAccess.Expression;
            var arguments = countInvocation.ArgumentList;

            // Create 'Any' identifier
            var anyName = SyntaxFactory.IdentifierName("Any");

            // Create new MemberAccess 'query.Any'
            var newMemberAccess = memberAccess.WithName(anyName);

            // Create Invocation 'query.Any(...)'
            var newInvocation = SyntaxFactory.InvocationExpression(newMemberAccess, arguments)
                .WithLeadingTrivia(binaryExpr.GetLeadingTrivia())
                .WithTrailingTrivia(binaryExpr.GetTrailingTrivia());

            // Replace the binary expression with the new invocation
            editor.ReplaceNode(binaryExpr, newInvocation);
        }

        return editor.GetChangedDocument();
    }
}
