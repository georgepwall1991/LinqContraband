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
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC059_DisposedContextConnection;

/// <summary>
/// Provides code fixes for LC059. Leaves disposal of the context's connection to the <c>DbContext</c>: a
/// <c>using</c>/<c>await using</c> declaration becomes a plain local declaration, a <c>using</c> statement becomes a
/// block that starts with a plain declaration, and an explicit <c>Dispose()</c>/<c>DisposeAsync()</c> statement is
/// removed.
/// </summary>
/// <remarks>
/// A using that also disposes another resource, and a disposal that is not a statement of its own in a block (the
/// body of an <c>if</c>, a lambda body), get no fix. The fixer compiles the rewritten document and only offers a
/// rewrite that adds no errors.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DisposedContextConnectionFixer))]
[Shared]
public sealed class DisposedContextConnectionFixer : CodeFixProvider
{
    private const string Title = "Let the DbContext dispose its connection";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DisposedContextConnectionAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var newRoot = Rewrite(root, node);
            if (newRoot == null) continue;

            var newDocument = document.WithSyntaxRoot(newRoot);
            newDocument = await Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel == null || CountErrors(newModel, cancellationToken) > errorsBefore.Value) continue;

            context.RegisterCodeFix(
                CodeAction.Create(Title, _ => Task.FromResult(newDocument), nameof(DisposedContextConnectionFixer)),
                diagnostic);
        }
    }

    private static SyntaxNode? Rewrite(SyntaxNode root, SyntaxNode node)
    {
        // Explicit Dispose() or DisposeAsync().
        if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { } invocation &&
            invocation.Span == node.Span &&
            invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
            })
        {
            return RemoveDisposeStatement(root, invocation);
        }

        var expression = node as ExpressionSyntax ?? node.FirstAncestorOrSelf<ExpressionSyntax>();
        if (expression == null) return null;

        // using (connection) or using (db.Database.GetDbConnection()).
        if (expression.Parent is UsingStatementSyntax { Expression: { } resource } usingExpression && resource == expression)
            return root.ReplaceNode(usingExpression, UnwrapUsingStatement(usingExpression, null));

        if (expression.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: 1 } declaration }
            })
        {
            return null;
        }

        switch (declaration.Parent)
        {
            case LocalDeclarationStatementSyntax local when local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword):
            {
                var plain = local
                    .WithAwaitKeyword(default)
                    .WithUsingKeyword(default)
                    .WithLeadingTrivia(local.GetLeadingTrivia());
                return root.ReplaceNode(local, plain);
            }

            case UsingStatementSyntax usingStatement when usingStatement.Declaration == declaration:
                return root.ReplaceNode(usingStatement, UnwrapUsingStatement(usingStatement, declaration));

            default:
                return null;
        }
    }

    /// <summary>
    /// The using statement's body as a plain block, starting with <paramref name="declaration"/> as an ordinary local
    /// declaration when there is one, so the local keeps its scope.
    /// </summary>
    private static StatementSyntax UnwrapUsingStatement(UsingStatementSyntax usingStatement, VariableDeclarationSyntax? declaration)
    {
        var leading = usingStatement.GetLeadingTrivia();
        var body = usingStatement.Statement;

        if (declaration == null)
            return body.WithLeadingTrivia(leading);

        var local = SyntaxFactory.LocalDeclarationStatement(declaration.WithoutTrivia());

        if (body is BlockSyntax block && block.Statements.Count > 0)
        {
            var openTrailing = block.OpenBraceToken.TrailingTrivia;
            var multiLine = openTrailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var firstLeading = block.Statements[0].GetLeadingTrivia();
            var indentation = firstLeading.Reverse().TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
            var endOfLine = openTrailing.FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));

            local = local
                .WithLeadingTrivia(multiLine ? SyntaxFactory.TriviaList(indentation) : SyntaxFactory.TriviaList())
                .WithTrailingTrivia(multiLine ? SyntaxFactory.TriviaList(endOfLine) : SyntaxFactory.TriviaList(SyntaxFactory.Space));

            return block
                .WithOpenBraceToken(block.OpenBraceToken.WithLeadingTrivia(leading))
                .WithStatements(block.Statements.Insert(0, local));
        }

        return SyntaxFactory.Block(local, body.WithoutLeadingTrivia())
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(usingStatement.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static SyntaxNode? RemoveDisposeStatement(SyntaxNode root, InvocationExpressionSyntax invocation)
    {
        SyntaxNode current = invocation;
        if (current.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configureAwait &&
            configureAwait.Expression == current &&
            configureAwait.Parent is InvocationExpressionSyntax configureAwaitCall)
        {
            current = configureAwaitCall;
        }

        if (current.Parent is AwaitExpressionSyntax awaitExpression)
            current = awaitExpression;

        if (current.Parent is not ExpressionStatementSyntax statement ||
            statement.Parent is not (BlockSyntax or SwitchSectionSyntax))
        {
            return null;
        }

        var annotation = new SyntaxAnnotation();
        var annotatedRoot = root.ReplaceNode(statement, statement.WithAdditionalAnnotations(annotation));
        var annotated = annotatedRoot.GetAnnotatedNodes(annotation).Single();
        var previous = annotated.GetFirstToken().GetPreviousToken();
        var ownLine = previous.TrailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));

        SyntaxNode prepared;
        if (ownLine)
        {
            // Keep comments above the removed line by handing them to the next token.
            var comments = annotated.GetLeadingTrivia();
            var lastEndOfLine = comments.LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            if (lastEndOfLine == default)
            {
                prepared = annotatedRoot;
            }
            else
            {
                var kept = comments.TakeWhile(trivia => trivia != lastEndOfLine).Append(lastEndOfLine);
                var next = annotated.GetLastToken().GetNextToken();
                prepared = annotatedRoot.ReplaceToken(next, next.WithLeadingTrivia(kept.Concat(next.LeadingTrivia)));
            }
        }
        else
        {
            // The statement shares a line with the previous one: the previous token takes over its trailing trivia.
            var trimmed = previous.TrailingTrivia.Reverse().SkipWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
            prepared = annotatedRoot.ReplaceToken(
                previous,
                previous.WithTrailingTrivia(trimmed.Concat(annotated.GetTrailingTrivia())));
        }

        var target = prepared.GetAnnotatedNodes(annotation).Single();
        return prepared.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia);
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
