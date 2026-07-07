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

namespace LinqContraband.Analyzers.LC001_LocalMethod;

/// <summary>
/// Provides code fixes for LC001. Switches LINQ queries to client-side evaluation using AsEnumerable() when local methods are called.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalMethodFixer))]
[Shared]
public sealed partial class LocalMethodFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalMethodAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var queryInvocation = FindQueryInvocation(invocation, semanticModel, context.CancellationToken);
        if (queryInvocation == null ||
            IsNestedQueryInvocation(semanticModel, queryInvocation, context.CancellationToken) ||
            !CanRewriteQueryInvocation(semanticModel, queryInvocation, context.CancellationToken))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Switch to client-side evaluation (AsEnumerable)",
                c => SwitchToClientSideAsync(context.Document, invocation, c),
                "SwitchToClientSide"),
            diagnostic);
    }

    private async Task<Document> SwitchToClientSideAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        editor.EnsureUsing("System.Linq");

        var queryInvocation = FindQueryInvocation(invocation, semanticModel, cancellationToken);
        if (queryInvocation == null) return document;

        if (queryInvocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        if (IsSystemLinqQueryableType(semanticModel, memberAccess.Expression, cancellationToken))
        {
            var enumerableQualifier = CreateEnumerableQualifier(memberAccess.Expression);
            if (!RewriteStaticQueryableInvocation(
                    editor,
                    semanticModel,
                    queryInvocation,
                    memberAccess,
                    enumerableQualifier,
                    cancellationToken))
                return document;

            RewriteEnclosingStaticQueryableContinuations(editor, semanticModel, queryInvocation, cancellationToken);

            return editor.GetChangedDocument();
        }

        // 3. Check if it is using extension method syntax: source.Where(...)
        var source = memberAccess.Expression;

        if (IsInvocationOf(source, "AsEnumerable")) return editor.GetChangedDocument();

        // 4. Create .AsEnumerable() call on the source
        var asEnumerableInvocation = CreateAsEnumerableInvocation(source);

        // 5. Replace the original source with the new source, preserving trivia
        editor.ReplaceNode(source, asEnumerableInvocation.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static InvocationExpressionSyntax CreateAsEnumerableInvocation(ExpressionSyntax source)
    {
        var receiver = ParenthesizeForMemberAccessIfNeeded(source);

        // construct: source.AsEnumerable()
        var asEnumerable = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("AsEnumerable"));

        return SyntaxFactory.InvocationExpression(asEnumerable);
    }

    private static ExpressionSyntax ParenthesizeForMemberAccessIfNeeded(ExpressionSyntax source)
    {
        return source switch
        {
            BaseExpressionSyntax or
            ElementAccessExpressionSyntax or
            IdentifierNameSyntax or
            InvocationExpressionSyntax or
            MemberAccessExpressionSyntax or
            ObjectCreationExpressionSyntax or
            ParenthesizedExpressionSyntax or
            ThisExpressionSyntax => source,
            _ => SyntaxFactory.ParenthesizedExpression(source.WithoutTrivia()).WithTriviaFrom(source)
        };
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }
}
