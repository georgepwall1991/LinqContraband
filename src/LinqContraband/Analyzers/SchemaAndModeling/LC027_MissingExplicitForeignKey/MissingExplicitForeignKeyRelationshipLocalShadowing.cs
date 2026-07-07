using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static bool IsShadowedByNestedLocal(
        IdentifierNameSyntax reference,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        foreach (var currentDeclarator in scope.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (currentDeclarator == declarator ||
                currentDeclarator.Identifier.ValueText != localName ||
                FindLocalScope(currentDeclarator) is not SyntaxNode currentScope ||
                currentScope == scope)
            {
                continue;
            }

            if (currentDeclarator.SpanStart > declarator.SpanStart &&
                IsVisibleAt(currentDeclarator, currentScope, reference))
            {
                return true;
            }
        }

        foreach (var designation in scope.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
        {
            if (designation.Identifier.ValueText != localName ||
                FindDesignationScope(designation) is not SyntaxNode designationScope ||
                designationScope == scope)
            {
                continue;
            }

            if (designation.SpanStart > declarator.SpanStart &&
                designationScope.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        foreach (var foreachStatement in scope.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (foreachStatement.Identifier.ValueText != localName ||
                FindStatementScope(foreachStatement) is not SyntaxNode foreachScope ||
                foreachScope == scope)
            {
                continue;
            }

            if (foreachStatement.SpanStart > declarator.SpanStart &&
                foreachStatement.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        foreach (var parameter in scope.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Identifier.ValueText != localName ||
                FindParameterScope(parameter) is not SyntaxNode parameterScope ||
                parameterScope == scope)
            {
                continue;
            }

            if (parameter.SpanStart > declarator.SpanStart &&
                parameterScope.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? FindDesignationScope(SingleVariableDesignationSyntax designation)
    {
        return designation.Ancestors().FirstOrDefault(node =>
            node is BlockSyntax or
                SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindStatementScope(StatementSyntax statement)
    {
        return statement.Ancestors().FirstOrDefault(node =>
            node is BlockSyntax or
                SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindParameterScope(ParameterSyntax parameter)
    {
        return parameter.Ancestors().FirstOrDefault(node =>
            node is SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindLocalScope(VariableDeclaratorSyntax declarator)
    {
        return declarator.Parent?.Parent?.Parent is BlockSyntax block
            ? block
            : declarator.Ancestors().FirstOrDefault(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax);
    }
}
