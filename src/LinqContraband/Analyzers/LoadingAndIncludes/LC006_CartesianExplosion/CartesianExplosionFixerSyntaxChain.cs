using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionFixer
{
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

    private static bool TryFindStaticIncludeSourceArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax source)
    {
        source = null!;
        InvocationExpressionSyntax? current = invocation;
        InvocationExpressionSyntax? firstInclude = null;

        while (current?.Expression is MemberAccessExpressionSyntax memberAccess &&
               IsEntityFrameworkQueryableExtensionsAccess(memberAccess, semanticModel, cancellationToken))
        {
            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName == "AsSplitQuery")
                return false;

            if (methodName == "Include")
                firstInclude = current;

            if (current.ArgumentList.Arguments.Count == 0 ||
                current.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax receiverInvocation)
            {
                break;
            }

            current = receiverInvocation;
        }

        if (firstInclude?.ArgumentList.Arguments.Count > 0)
        {
            source = firstInclude.ArgumentList.Arguments[0].Expression;
            return true;
        }

        return false;
    }

    private static bool IsEntityFrameworkQueryableExtensionsAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        return symbol is INamedTypeSymbol
        {
            Name: "EntityFrameworkQueryableExtensions",
            ContainingNamespace: { } containingNamespace
        } && containingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static ExpressionSyntax ParenthesizeIfNeededForMemberAccess(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or
            InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or ThisExpressionSyntax or
            BaseExpressionSyntax)
        {
            return expression;
        }

        return SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia())
            .WithTriviaFrom(expression);
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == methodName;
        }

        return false;
    }
}
