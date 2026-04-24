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

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

/// <summary>
/// Provides code fixes for LC006. Adds AsSplitQuery() to prevent cartesian explosion from multiple Include operations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CartesianExplosionFixer))]
[Shared]
public class CartesianExplosionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CartesianExplosionAnalyzer.DiagnosticId);

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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use AsSplitQuery()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseAsSplitQuery"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        if (FindEffectiveAsSingleQueryInvocation(invocation) is { Expression: MemberAccessExpressionSyntax asSingleMemberAccess } asSingleQuery)
        {
            var replacementMemberAccess = asSingleMemberAccess.WithName(
                SyntaxFactory.IdentifierName("AsSplitQuery").WithTriviaFrom(asSingleMemberAccess.Name));
            editor.ReplaceNode(asSingleQuery, asSingleQuery.WithExpression(replacementMemberAccess));
            return editor.GetChangedDocument();
        }

        var firstInclude = FindFirstIncludeInvocation(invocation);
        if (firstInclude?.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var source = memberAccess.Expression;
        if (IsInvocationOf(source, "AsSplitQuery")) return document;

        // Create .AsSplitQuery() invocation
        var asSplitQuery = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source,
            SyntaxFactory.IdentifierName("AsSplitQuery"));

        var asSplitQueryInvocation = SyntaxFactory.InvocationExpression(asSplitQuery);

        // Replace 'source' in the original expression with 'source.AsSplitQuery()', preserving trivia
        editor.ReplaceNode(source, asSplitQueryInvocation.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static InvocationExpressionSyntax? FindEffectiveAsSingleQueryInvocation(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax? current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation &&
               currentInvocation.Expression is MemberAccessExpressionSyntax currentMemberAccess)
        {
            if (currentMemberAccess.Name.Identifier.Text == "AsSingleQuery")
                return currentInvocation;

            if (currentMemberAccess.Name.Identifier.Text == "AsSplitQuery")
                return null;

            current = currentMemberAccess.Expression;
        }

        return null;
    }

    private static InvocationExpressionSyntax? FindFirstIncludeInvocation(InvocationExpressionSyntax invocation)
    {
        InvocationExpressionSyntax? firstInclude = null;
        ExpressionSyntax? current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation &&
               currentInvocation.Expression is MemberAccessExpressionSyntax currentMemberAccess)
        {
            if (currentMemberAccess.Name.Identifier.Text == "Include")
                firstInclude = currentInvocation;

            current = currentMemberAccess.Expression;
        }

        return firstInclude;
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }
}
