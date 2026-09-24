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

namespace LinqContraband.Analyzers.LC053_OverwrittenQueryFilter;

/// <summary>
/// Provides a code fix for LC053. Folds an earlier unnamed filter into the later one with <c>&amp;&amp;</c>.
/// </summary>
/// <remarks>
/// The fix is offered only for the plain shape: exactly two unnamed filters, each its own
/// <c>builder.HasQueryFilter(x =&gt; ...)</c> statement in the same block, with a receiver that has no side effects.
/// The earlier statement is removed and its condition is added to the later filter, which is the one EF Core keeps.
/// Filters in different methods or classes, chained calls, and mixed named and unnamed filters stay manual.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OverwrittenQueryFilterFixer))]
[Shared]
public sealed class OverwrittenQueryFilterFixer : CodeFixProvider
{
    private const string Title = "Combine the query filters with &&";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OverwrittenQueryFilterAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(OverwrittenQueryFilterAnalyzer.OtherFilterCountProperty, out var count) ||
                count != "1" ||
                diagnostic.AdditionalLocations.Count != 1 ||
                diagnostic.AdditionalLocations[0].SourceTree != root.SyntaxTree)
            {
                continue;
            }

            if (!TryGetFilterStatement(root, diagnostic.Location, out var earlier) ||
                !TryGetFilterStatement(root, diagnostic.AdditionalLocations[0], out var later))
            {
                continue;
            }

            // Register on the earlier call only, so Fix All does not produce the same merge twice.
            if (earlier.Statement.Parent is not BlockSyntax block ||
                later.Statement.Parent != block ||
                block.Statements.IndexOf(earlier.Statement) >= block.Statements.IndexOf(later.Statement) ||
                !IsSideEffectFree(earlier.Receiver))
            {
                continue;
            }

            if (TryRenameParameter(semanticModel, earlier, later.ParameterName, context.CancellationToken) is not { } earlierCondition)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    ct => CombineAsync(context.Document, earlier, later, earlierCondition, ct),
                    nameof(OverwrittenQueryFilterFixer)),
                diagnostic);
        }
    }

    private static async Task<Document> CombineAsync(
        Document document,
        FilterStatement earlier,
        FilterStatement later,
        ExpressionSyntax earlierCondition,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var combined = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                Parenthesize(earlierCondition.WithoutTrivia()),
                SyntaxFactory.Token(SyntaxKind.AmpersandAmpersandToken)
                    .WithLeadingTrivia(SyntaxFactory.Space)
                    .WithTrailingTrivia(SyntaxFactory.Space),
                Parenthesize(later.Body.WithoutTrivia()))
            .WithTriviaFrom(later.Body);

        // When the filters are adjacent, the kept statement takes over the removed one's leading trivia (blank lines,
        // comments). Otherwise the statement in between is left alone, so Fix All merges never touch the same node.
        var block = (BlockSyntax)earlier.Statement.Parent!;
        var adjacent = block.Statements[block.Statements.IndexOf(earlier.Statement) + 1] == later.Statement;
        if (adjacent)
        {
            editor.ReplaceNode(
                later.Statement,
                later.Statement.ReplaceNode(later.Body, combined).WithLeadingTrivia(earlier.Statement.GetLeadingTrivia()));
        }
        else
        {
            editor.ReplaceNode(later.Body, combined);
        }

        editor.RemoveNode(earlier.Statement, SyntaxRemoveOptions.KeepNoTrivia);
        return editor.GetChangedDocument();
    }

    private static bool TryGetFilterStatement(SyntaxNode root, Location location, out FilterStatement filter)
    {
        filter = default;
        var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        if (node is not SimpleNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } ||
            memberAccess.Name != node ||
            memberAccess.Parent is not InvocationExpressionSyntax { Parent: ExpressionStatementSyntax statement } invocation ||
            invocation.Expression != memberAccess ||
            invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        switch (invocation.ArgumentList.Arguments[0].Expression)
        {
            case SimpleLambdaExpressionSyntax { ExpressionBody: { } body } simple when simple.AsyncKeyword.IsKind(SyntaxKind.None):
                filter = new FilterStatement(statement, memberAccess.Expression, simple.Parameter, body);
                return true;
            case ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body, ParameterList.Parameters.Count: 1 } parenthesized
                when parenthesized.AsyncKeyword.IsKind(SyntaxKind.None):
                filter = new FilterStatement(statement, memberAccess.Expression, parenthesized.ParameterList.Parameters[0], body);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The earlier statement is deleted, so its receiver must not do anything else: a builder variable or
    /// <c>modelBuilder.Entity&lt;T&gt;()</c>, which the later call registers anyway.
    /// </summary>
    private static bool IsSideEffectFree(ExpressionSyntax receiver)
    {
        return receiver switch
        {
            IdentifierNameSyntax => true,
            InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax,
                    Name: GenericNameSyntax { Identifier.ValueText: "Entity", TypeArgumentList.Arguments.Count: 1 }
                }
            } => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns the earlier condition with its lambda parameter renamed to the later lambda's, or null when the new name
    /// already means something else inside the earlier condition.
    /// </summary>
    private static ExpressionSyntax? TryRenameParameter(
        SemanticModel semanticModel,
        FilterStatement earlier,
        string newName,
        CancellationToken cancellationToken)
    {
        var parameter = semanticModel.GetDeclaredSymbol(earlier.Parameter, cancellationToken);
        if (parameter == null)
            return null;

        var references = earlier.Body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter))
            .ToList();

        if (earlier.ParameterName == newName)
            return earlier.Body;

        var collides = earlier.Body.DescendantTokens()
            .Any(token => token.IsKind(SyntaxKind.IdentifierToken) &&
                          token.ValueText == newName &&
                          !references.Any(reference => reference.Identifier == token));
        if (collides)
            return null;

        return earlier.Body.ReplaceNodes(
            references,
            (_, rewritten) => SyntaxFactory.IdentifierName(newName).WithTriviaFrom(rewritten));
    }

    /// <summary>Wraps operands that bind more loosely than <c>&amp;&amp;</c>.</summary>
    private static ExpressionSyntax Parenthesize(ExpressionSyntax expression)
    {
        return expression is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression or (int)SyntaxKind.CoalesceExpression }
            or ConditionalExpressionSyntax
            or AssignmentExpressionSyntax
            or LambdaExpressionSyntax
            ? SyntaxFactory.ParenthesizedExpression(expression)
            : expression;
    }

    private readonly struct FilterStatement
    {
        public FilterStatement(
            ExpressionStatementSyntax statement,
            ExpressionSyntax receiver,
            ParameterSyntax parameter,
            ExpressionSyntax body)
        {
            Statement = statement;
            Receiver = receiver;
            Parameter = parameter;
            Body = body;
        }

        public ExpressionStatementSyntax Statement { get; }

        public ExpressionSyntax Receiver { get; }

        public ParameterSyntax Parameter { get; }

        public ExpressionSyntax Body { get; }

        public string ParameterName => Parameter.Identifier.ValueText;
    }
}
