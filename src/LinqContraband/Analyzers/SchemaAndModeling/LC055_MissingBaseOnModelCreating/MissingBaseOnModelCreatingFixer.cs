using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating;

/// <summary>
/// Provides code fixes for LC055. Inserts <c>base.OnModelCreating(modelBuilder);</c> as the first statement, so the
/// override's own configuration still runs after, and can refine, the base configuration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingBaseOnModelCreatingFixer))]
[Shared]
public sealed class MissingBaseOnModelCreatingFixer : CodeFixProvider
{
    private const string Title = "Call base.OnModelCreating first";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingBaseOnModelCreatingAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var method = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method == null || method.ParameterList.Parameters.Count != 1) continue;

            var replacement = TryAddBaseCall(method);
            if (replacement == null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(method, replacement))),
                    nameof(MissingBaseOnModelCreatingFixer)),
                diagnostic);
        }
    }

    private static MethodDeclarationSyntax? TryAddBaseCall(MethodDeclarationSyntax method)
    {
        var parameterName = method.ParameterList.Parameters[0].Identifier;
        var baseCall = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.BaseExpression(),
                    SyntaxFactory.IdentifierName("OnModelCreating")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameterName.WithoutTrivia()))))));

        if (method.Body != null)
        {
            var statements = method.Body.Statements;
            if (statements.Count == 0)
            {
                return method.WithBody(method.Body.WithStatements(
                    SyntaxFactory.SingletonList<StatementSyntax>(baseCall.WithAdditionalAnnotations(Formatter.Annotation))));
            }

            // Take the first statement's indentation and the file's line ending; the first statement keeps its trivia.
            var firstTrivia = statements[0].GetLeadingTrivia();
            var indentation = firstTrivia.Count > 0 && firstTrivia[firstTrivia.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia)
                ? SyntaxFactory.TriviaList(firstTrivia[firstTrivia.Count - 1])
                : SyntaxFactory.TriviaList();
            var endOfLine = SyntaxFactory.LineFeed;
            foreach (var trivia in method.Body.OpenBraceToken.TrailingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia)) endOfLine = trivia;
            }

            baseCall = baseCall.WithLeadingTrivia(indentation).WithTrailingTrivia(endOfLine);
            return method.WithBody(method.Body.WithStatements(statements.Insert(0, baseCall)));
        }

        // => expression; becomes a block that calls base first. Only a statement expression can move into a block.
        if (method.ExpressionBody is not { Expression: var expression } ||
            expression is not (InvocationExpressionSyntax or AssignmentExpressionSyntax or AwaitExpressionSyntax))
        {
            return null;
        }

        var block = SyntaxFactory.Block(
                baseCall,
                SyntaxFactory.ExpressionStatement(expression.WithoutTrivia()))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return method
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithParameterList(method.ParameterList.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
            .WithBody(block)
            .WithTrailingTrivia(method.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
