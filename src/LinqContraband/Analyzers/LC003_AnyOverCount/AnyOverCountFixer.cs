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

        if (!TryExtractCountInvocation(binaryExpr.Left, out var leftInvocation, out var leftAwaited))
            leftInvocation = null;

        if (!TryExtractCountInvocation(binaryExpr.Right, out var rightInvocation, out var rightAwaited))
            rightInvocation = null;

        var countInvocation = leftInvocation ?? rightInvocation;
        var isAwaited = leftInvocation != null ? leftAwaited : rightAwaited;
        if (countInvocation == null || countInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var anyMethodName = GetReplacementMethodName(memberAccess.Name.Identifier.Text);
        if (anyMethodName == null)
            return document;

        var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(anyMethodName));
        ExpressionSyntax replacement = SyntaxFactory.InvocationExpression(newMemberAccess, countInvocation.ArgumentList);

        if (isAwaited)
            replacement = SyntaxFactory.AwaitExpression(replacement);

        if (binaryExpr.IsKind(SyntaxKind.EqualsExpression) && HasZeroConstant(binaryExpr))
            replacement = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(replacement.WithoutTrivia()))
                .WithTriviaFrom(replacement);

        replacement = replacement
            .WithLeadingTrivia(binaryExpr.GetLeadingTrivia())
            .WithTrailingTrivia(binaryExpr.GetTrailingTrivia());

        editor.ReplaceNode(binaryExpr, replacement);

        return editor.GetChangedDocument();
    }

    private static bool TryExtractCountInvocation(
        ExpressionSyntax expression,
        out InvocationExpressionSyntax? invocation,
        out bool isAwaited)
    {
        isAwaited = false;
        invocation = null;

        var current = expression;
        while (current is CastExpressionSyntax cast)
            current = cast.Expression;

        while (current is ParenthesizedExpressionSyntax parenthesized)
            current = parenthesized.Expression;

        if (current is AwaitExpressionSyntax awaitExpression)
        {
            isAwaited = true;
            current = awaitExpression.Expression;
        }

        while (current is ParenthesizedExpressionSyntax nestedParenthesized)
            current = nestedParenthesized.Expression;

        invocation = current as InvocationExpressionSyntax;
        return invocation != null;
    }

    private static string? GetReplacementMethodName(string methodName)
    {
        return methodName switch
        {
            "Count" => "Any",
            "LongCount" => "Any",
            "CountAsync" => "AnyAsync",
            "LongCountAsync" => "AnyAsync",
            _ => null
        };
    }

    private static bool HasZeroConstant(BinaryExpressionSyntax binaryExpression)
    {
        return IsZeroLiteral(binaryExpression.Left) || IsZeroLiteral(binaryExpression.Right);
    }

    private static bool IsZeroLiteral(ExpressionSyntax expression)
    {
        expression = expression is ParenthesizedExpressionSyntax parenthesized
            ? parenthesized.Expression
            : expression;

        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
               literal.Token.ValueText == "0";
    }
}
