using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static StatementSyntax CreateExpressionBodyStatement(
        SyntaxNode? member,
        ExpressionSyntax expression,
        SyntaxTrivia endOfLine,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (RequiresExpressionStatement(member, semanticModel, cancellationToken))
            return SyntaxFactory.ExpressionStatement(expression).WithTrailingTrivia(endOfLine);

        return SyntaxFactory.ReturnStatement(expression).WithTrailingTrivia(endOfLine);
    }

    private static bool RequiresExpressionStatement(
        SyntaxNode? member,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        member switch
        {
            MethodDeclarationSyntax method => IsVoid(method.ReturnType) ||
                                              (HasAsyncModifier(method.Modifiers) &&
                                               IsNonGenericTaskLike(method.ReturnType, semanticModel, cancellationToken)),
            LocalFunctionStatementSyntax localFunction => IsVoid(localFunction.ReturnType) ||
                                                          (HasAsyncModifier(localFunction.Modifiers) &&
                                                           IsNonGenericTaskLike(localFunction.ReturnType, semanticModel, cancellationToken)),
            _ => false
        };

    private static bool IsVoid(TypeSyntax returnType) =>
        returnType is PredefinedTypeSyntax predefined &&
        predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static bool HasAsyncModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));

    private static bool IsNonGenericTaskLike(
        TypeSyntax returnType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetTypeInfo(returnType, cancellationToken).Type is INamedTypeSymbol namedType &&
            namedType.Arity == 0 &&
            namedType.Name is "Task" or "ValueTask" &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            return true;
        }

        return IsNonGenericTaskLikeBySyntax(returnType);
    }

    private static bool IsNonGenericTaskLikeBySyntax(TypeSyntax returnType) =>
        returnType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text is "Task" or "ValueTask",
            QualifiedNameSyntax qualified => IsNonGenericTaskLikeBySyntax(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => IsNonGenericTaskLikeBySyntax(aliasQualified.Name),
            _ => false
        };
}
