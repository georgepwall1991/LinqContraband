using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static Document ConvertExpressionBodiedMember(
        Document document,
        DocumentEditor editor,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody == null) return document;

        var replacements = BuildExpressionBodyReplacements(
            expressionBody.Expression,
            memberAccess,
            semanticModel,
            cancellationToken);
        if (replacements.Count == 0) return document;

        var rewrittenAccesses = FindExpressionBodyClockAccesses(
            expressionBody.Expression,
            semanticModel,
            cancellationToken);
        var updatedExpression = expressionBody.Expression.ReplaceNodes(
            rewrittenAccesses,
            (original, _) =>
            {
                var replacement = FindReplacementFor(original, replacements, semanticModel, cancellationToken);
                return replacement is null
                    ? original
                    : SyntaxFactory.IdentifierName(replacement.VariableName).WithTriviaFrom(original);
            });

        var endOfLine = expressionBody.GetDocumentEndOfLine();
        var statements = replacements
            .Select(replacement => (StatementSyntax)CreateLocalDeclaration(replacement.Initializer, replacement.VariableName)
                .WithTrailingTrivia(endOfLine))
            .ToList();
        var bodyStatement = CreateExpressionBodyStatement(
            expressionBody.Parent,
            updatedExpression,
            endOfLine,
            semanticModel,
            cancellationToken);
        statements.Add(bodyStatement);

        var body = SyntaxFactory.Block(statements)
            .WithAdditionalAnnotations(Formatter.Annotation);

        switch (expressionBody.Parent)
        {
            case MethodDeclarationSyntax method:
                editor.ReplaceNode(
                    method,
                    method.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(body)
                        .WithAdditionalAnnotations(Formatter.Annotation));
                return editor.GetChangedDocument();

            case LocalFunctionStatementSyntax localFunction:
                editor.ReplaceNode(
                    localFunction,
                    localFunction.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(body)
                        .WithAdditionalAnnotations(Formatter.Annotation));
                return editor.GetChangedDocument();

            default:
                return document;
        }
    }

    private static bool IsInsideQueryableLambda(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var lambda = node.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (lambda is null)
            return false;

        foreach (var argument in lambda.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation)
                continue;

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
                method.ContainingType.Name == "Queryable" &&
                method.ContainingNamespace.ToDisplayString() == "System.Linq")
            {
                return true;
            }
        }

        return false;
    }
}
