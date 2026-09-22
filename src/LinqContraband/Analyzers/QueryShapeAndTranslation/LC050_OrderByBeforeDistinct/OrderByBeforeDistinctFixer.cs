using System.Collections.Generic;
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

namespace LinqContraband.Analyzers.LC050_OrderByBeforeDistinct;

/// <summary>
/// Provides code fixes for LC050. Moves the discarded sort after Distinct().
/// </summary>
/// <remarks>
/// Two shapes are rewritten: a sort chain directly before Distinct
/// (<c>q.OrderBy(k).ThenBy(k2).Distinct()</c> becomes <c>q.Distinct().OrderBy(k).ThenBy(k2)</c>), and a single
/// sort whose key is exactly the projected value (<c>q.OrderBy(x =&gt; x.Name).Select(x =&gt; x.Name).Distinct()</c>
/// becomes <c>q.Select(x =&gt; x.Name).Distinct().OrderBy(x =&gt; x)</c>). Anything else is reported without a fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OrderByBeforeDistinctFixer))]
[Shared]
public sealed partial class OrderByBeforeDistinctFixer : CodeFixProvider
{
    private const string Title = "Sort after Distinct()";

    private static readonly HashSet<string> SortMethods = new()
    {
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    };

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OrderByBeforeDistinctAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is not SimpleNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } ||
                memberAccess.Parent is not InvocationExpressionSyntax distinct ||
                distinct.Expression != memberAccess)
            {
                continue;
            }

            var replacement = TryBuildReplacement(distinct, memberAccess);
            if (replacement == null) continue;
            if (!ResultTypeChangeIsSafe(distinct, semanticModel, context.CancellationToken)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(distinct, replacement))),
                    nameof(OrderByBeforeDistinctFixer)),
                diagnostic);
        }
    }

    private static ExpressionSyntax? TryBuildReplacement(
        InvocationExpressionSyntax distinct,
        MemberAccessExpressionSyntax distinctAccess)
    {
        if (distinct.ArgumentList.Arguments.Count != 0) return null;

        // Shape 1: q.OrderBy(k)[.ThenBy(k2)...].Distinct()
        var sortChain = CollectSortChain(distinctAccess.Expression, out var sortSource);
        if (sortChain.Count > 0)
        {
            if (!IsPrimarySort(sortChain[sortChain.Count - 1])) return null;

            var distinctOnSource = CreateDistinct(sortSource, distinctAccess);
            var moved = distinctAccess.Expression.ReplaceNode(sortSource, distinctOnSource);
            return moved.WithTriviaFrom(distinct);
        }

        // Shape 2: q.OrderBy(x => key).Select(y => key).Distinct()
        if (distinctAccess.Expression is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Select" } selectAccess
            } select ||
            select.ArgumentList.Arguments.Count != 1 ||
            selectAccess.Expression is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax orderAccess
            } order ||
            !IsPrimarySort(order) ||
            order.ArgumentList.Arguments.Count != 1 ||
            !TryGetSimpleLambda(select.ArgumentList.Arguments[0].Expression, out var selectParameter, out var selectBody) ||
            !TryGetSimpleLambda(order.ArgumentList.Arguments[0].Expression, out var orderParameter, out var orderBody) ||
            !AreSameKey(orderParameter, orderBody, selectParameter, selectBody))
        {
            return null;
        }

        var projection = select.ReplaceNode(order, orderAccess.Expression);
        var sortedDistinct = CreateDistinct(projection, distinctAccess);
        var identityKey = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(selectParameter)),
            SyntaxFactory.IdentifierName(selectParameter));

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    sortedDistinct,
                    orderAccess.OperatorToken.WithLeadingTrivia(distinctAccess.OperatorToken.LeadingTrivia),
                    orderAccess.Name),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(identityKey))))
            .WithTriviaFrom(distinct);
    }

    private static List<InvocationExpressionSyntax> CollectSortChain(ExpressionSyntax expression, out ExpressionSyntax source)
    {
        var chain = new List<InvocationExpressionSyntax>();
        source = expression;

        while (source is InvocationExpressionSyntax
               {
                   Expression: MemberAccessExpressionSyntax memberAccess
               } invocation &&
               SortMethods.Contains(memberAccess.Name.Identifier.ValueText))
        {
            chain.Add(invocation);
            if (IsPrimarySort(invocation))
            {
                source = memberAccess.Expression;
                return chain;
            }

            source = memberAccess.Expression;
        }

        return chain;
    }

    private static bool IsPrimarySort(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "OrderBy" or "OrderByDescending"
        };
    }

    private static InvocationExpressionSyntax CreateDistinct(ExpressionSyntax source, MemberAccessExpressionSyntax distinctAccess)
    {
        // End-of-line trivia belongs to the token before a `.`, so `source` keeps its line break and the new
        // `Distinct()` repeats it; the `.` keeps Distinct's own indentation. Single-line chains have neither.
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                source,
                distinctAccess.OperatorToken,
                distinctAccess.Name.WithoutTrivia()),
            SyntaxFactory.ArgumentList().WithTrailingTrivia(source.GetTrailingTrivia()));
    }
}
