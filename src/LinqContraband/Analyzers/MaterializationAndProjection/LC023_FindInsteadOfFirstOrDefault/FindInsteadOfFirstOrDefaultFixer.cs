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

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Provides code fixes for LC023. Replaces FirstOrDefault/SingleOrDefault primary key lookups with Find/FindAsync.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FindInsteadOfFirstOrDefaultFixer))]
[Shared]
public sealed partial class FindInsteadOfFirstOrDefaultFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FindInsteadOfFirstOrDefaultAnalyzer.DiagnosticId);

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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        if (!TryCreateFixContext(invocation, semanticModel, context.CancellationToken, out var fixContext))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Find/FindAsync",
                c => ApplyFixAsync(context.Document, invocation, fixContext, c),
                "UseFind"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        FixContext fixContext,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var newMethodName = fixContext.IsAsync ? "FindAsync" : "Find";
        var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(newMethodName));
        var newArguments = CreateFindArgumentList(fixContext);

        var newInvocation = SyntaxFactory.InvocationExpression(newMemberAccess, newArguments)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        editor.ReplaceNode(invocation, newInvocation);
        return editor.GetChangedDocument();
    }

    private static ArgumentListSyntax CreateFindArgumentList(FixContext fixContext)
    {
        if (!fixContext.IsAsync || fixContext.CancellationTokenArgument == null)
        {
            return SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(fixContext.KeyValueExpression)));
        }

        var keyArray = SyntaxFactory.ArrayCreationExpression(
            SyntaxFactory.ArrayType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                SyntaxFactory.SingletonList(
                    SyntaxFactory.ArrayRankSpecifier(
                        SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                            SyntaxFactory.OmittedArraySizeExpression())))),
            SyntaxFactory.InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SyntaxFactory.SingletonSeparatedList(fixContext.KeyValueExpression)));

        return SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(keyArray),
                fixContext.CancellationTokenArgument.WithoutTrivia()
            }));
    }
}
