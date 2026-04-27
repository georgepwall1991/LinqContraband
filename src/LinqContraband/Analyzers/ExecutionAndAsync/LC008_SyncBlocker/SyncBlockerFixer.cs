using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Constants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

/// <summary>
/// Provides code fixes for LC008. Replaces synchronous blocking methods with their async/await equivalents in Entity Framework queries.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncBlockerFixer))]
[Shared]
public class SyncBlockerFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SyncBlockerAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindNode(diagnosticSpan) as InvocationExpressionSyntax;
        if (invocation == null) return;

        if (IsInvalidAwaitContext(invocation)) return;

        // We need the method name to find the replacement
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            if (SyncAsyncMappings.SyncToAsyncMap.TryGetValue(methodName, out var asyncMethodName))
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Use {asyncMethodName} and await",
                        c => ApplyFixAsync(context.Document, invocation, asyncMethodName, c),
                        "UseAsyncMethod"),
                    diagnostic);
        }
    }

    private static bool IsInvalidAwaitContext(SyntaxNode invocation)
    {
        for (SyntaxNode? node = invocation; node != null; node = node.Parent)
        {
            var parent = node.Parent;
            if (parent == null) continue;

            switch (parent)
            {
                case FromClauseSyntax fromClause when fromClause.Expression == node:
                    // 'await' is only legal in the FIRST from clause of a query
                    // expression. Subsequent (continuation) from clauses appear
                    // as children of QueryBodySyntax and do not allow await.
                    return fromClause.Parent is not QueryExpressionSyntax;

                case JoinClauseSyntax joinClause when joinClause.InExpression == node:
                    // 'await' is allowed in the collection expression of a join.
                    return false;

                case QueryClauseSyntax:
                case SelectOrGroupClauseSyntax:
                case OrderingSyntax:
                case QueryContinuationSyntax:
                    return true;
            }

            if (parent is AnonymousFunctionExpressionSyntax anonymousFunction)
                return !anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);

            if (parent is LocalFunctionStatementSyntax localFunction)
                return !localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);

            if (parent is BaseMethodDeclarationSyntax method)
                return !method.Modifiers.Any(SyntaxKind.AsyncKeyword);
        }

        return false;
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        string asyncMethodName, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        // 1. Replace method name (e.g. ToList -> ToListAsync)
        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName(asyncMethodName));

        var newInvocation = invocation.WithExpression(newMemberAccess);

        // 2. Add 'await' expression
        var awaitExpression = SyntaxFactory.AwaitExpression(newInvocation)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        // We need to clear trivia from the inner invocation to avoid duplication/messy formatting
        newInvocation = newInvocation.WithoutLeadingTrivia().WithoutTrailingTrivia();

        // Re-attach to await
        awaitExpression = awaitExpression.WithExpression(newInvocation);

        // 3. Add Formatting annotation
        var formattedAwait = awaitExpression.WithAdditionalAnnotations(Formatter.Annotation);

        editor.ReplaceNode(invocation, formattedAwait);

        return editor.GetChangedDocument();
    }
}
