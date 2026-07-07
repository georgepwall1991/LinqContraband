using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesFixer
{
    private static ExpressionSyntax CreateToFrozenSetInvocation(ExpressionSyntax sourceExpression, ExpressionSyntax? comparerArgument)
    {
        var receiver = ParenthesizeIfNeeded(sourceExpression.WithoutTrivia());
        var memberAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("ToFrozenSet"));

        if (comparerArgument is null)
            return SyntaxFactory.InvocationExpression(memberAccess);

        return SyntaxFactory.InvocationExpression(
            memberAccess,
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(comparerArgument.WithoutTrivia()))));
    }

    private static TypeSyntax CreateTypeSyntax(ITypeSymbol typeSymbol)
    {
        return SyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithAdditionalAnnotations(Simplifier.Annotation);
    }

    private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax => expression,
            GenericNameSyntax => expression,
            MemberAccessExpressionSyntax => expression,
            InvocationExpressionSyntax => expression,
            ElementAccessExpressionSyntax => expression,
            ThisExpressionSyntax => expression,
            BaseExpressionSyntax => expression,
            ParenthesizedExpressionSyntax => expression,
            ArrayCreationExpressionSyntax => expression,
            ImplicitArrayCreationExpressionSyntax => expression,
            ImplicitObjectCreationExpressionSyntax => expression,
            ObjectCreationExpressionSyntax => expression,
            _ => SyntaxFactory.ParenthesizedExpression(expression)
        };
    }
}
