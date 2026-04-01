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

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

/// <summary>
/// Provides code fixes for LC004. Materializes the offending query explicitly with ToList().
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IQueryableLeakFixer))]
[Shared]
public sealed class IQueryableLeakFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(IQueryableLeakAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(IQueryableLeakDiagnosticProperties.FixerEligible, out var fixerEligible) ||
            fixerEligible != "true")
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var expression = node as ExpressionSyntax ?? node.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault();
        if (expression == null)
            return;

        var replacementTarget = expression.Parent is ParenthesizedExpressionSyntax parenthesizedExpression
            ? parenthesizedExpression
            : expression;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Materialize with ToList()",
                cancellationToken => ApplyFixAsync(context.Document, replacementTarget, cancellationToken),
                nameof(IQueryableLeakFixer)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var receiver = ParenthesizeIfNeeded(expression.WithoutTrivia());
        var fixedExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    SyntaxFactory.IdentifierName("ToList")))
            .WithTriviaFrom(expression);

        editor.ReplaceNode(expression, fixedExpression);
        editor.EnsureUsing("System.Linq");

        return editor.GetChangedDocument();
    }

    private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax => expression,
            GenericNameSyntax => expression,
            MemberAccessExpressionSyntax => expression,
            InvocationExpressionSyntax => expression,
            ElementAccessExpressionSyntax => expression,
            ThisExpressionSyntax => expression,
            BaseExpressionSyntax => expression,
            ParenthesizedExpressionSyntax => expression,
            _ => SyntaxFactory.ParenthesizedExpression(expression)
        };
    }
}
