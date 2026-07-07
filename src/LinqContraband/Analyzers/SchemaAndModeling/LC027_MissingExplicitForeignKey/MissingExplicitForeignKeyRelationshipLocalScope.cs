using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static bool HasSingleLocalAssignment(
        SyntaxNode scope,
        VariableDeclaratorSyntax declarator,
        CancellationToken cancellationToken)
    {
        var assignmentCount = 1;
        var localName = declarator.Identifier.ValueText;

        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ContainsLocalWriteTarget(assignment.Left, declarator, scope, localName))
                assignmentCount++;
        }

        foreach (var argument in scope.DescendantNodes().OfType<ArgumentSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (argument.RefKindKeyword.ValueText is not ("ref" or "out"))
                continue;

            if (ContainsLocalWriteTarget(argument.Expression, declarator, scope, localName))
                assignmentCount++;
        }

        return assignmentCount == 1;
    }

    private static bool ContainsLocalWriteTarget(
        ExpressionSyntax expression,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => IsLocalWriteTarget(identifier, declarator, scope, localName),
            ParenthesizedExpressionSyntax parenthesized => ContainsLocalWriteTarget(parenthesized.Expression, declarator, scope, localName),
            TupleExpressionSyntax tuple => tuple.Arguments.Any(argument =>
                ContainsLocalWriteTarget(argument.Expression, declarator, scope, localName)),
            _ => false
        };
    }

    private static bool IsLocalWriteTarget(
        IdentifierNameSyntax identifier,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        return identifier.Identifier.ValueText == localName &&
               IsVisibleAt(declarator, scope, identifier) &&
               !IsShadowedByNestedLocal(identifier, declarator, scope, localName);
    }

    private static bool IsVisibleAt(
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        SyntaxNode reference)
    {
        return reference.SpanStart >= declarator.SpanStart &&
               scope.Span.Contains(reference.SpanStart);
    }
}
