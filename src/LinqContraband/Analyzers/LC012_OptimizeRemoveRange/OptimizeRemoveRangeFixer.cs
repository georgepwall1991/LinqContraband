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

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

/// <summary>
/// Provides code fixes for LC012. Replaces RemoveRange with ExecuteDelete.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OptimizeRemoveRangeFixer))]
[Shared]
public class OptimizeRemoveRangeFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OptimizeRemoveRangeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteDelete()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseExecuteDelete"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        
        // RemoveRange(query) -> query.ExecuteDelete()
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var queryExpression = invocation.ArgumentList.Arguments[0].Expression;
            
            // Handle ExecuteDeleteAsync if needed, but for now we focus on the basic transformation
            var executeDeleteName = SyntaxFactory.IdentifierName("ExecuteDelete");
            var memberAccess = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, queryExpression, executeDeleteName);
            var newInvocation = SyntaxFactory.InvocationExpression(memberAccess);

            // Add warning comment
            var warningComment = SyntaxFactory.Comment("// Warning: ExecuteDelete bypasses change tracking and cascades.");
            var newInvocationWithComment = newInvocation.WithLeadingTrivia(invocation.GetLeadingTrivia().Add(warningComment).Add(SyntaxFactory.ElasticLineFeed));

            editor.ReplaceNode(invocation, newInvocationWithComment);
        }

        return editor.GetChangedDocument();
    }
}
