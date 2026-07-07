using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static string? ExtractNavigationNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;
        string? withOneNavigationName = null;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "HasOne")
                {
                    var navName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                    if (navName != null)
                        return navName;
                }
                else if (methodName == "WithOne")
                {
                    withOneNavigationName ??= ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                }

                current = memberAccess.Expression;
                continue;
            }

            current = current switch
            {
                MemberAccessExpressionSyntax nextMemberAccess => nextMemberAccess.Expression,
                _ => null
            };
        }

        return withOneNavigationName;
    }

    private static string? ExtractNavigationNameFromArgument(ExpressionSyntax? argument)
    {
        return argument switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => ExtractMemberName(simpleLambda.Body),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => ExtractMemberName(parenthesizedLambda.Body),
            _ => null
        };
    }

    private static string? ExtractMemberName(SyntaxNode body)
    {
        return body switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "Entity")
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                    return typeArg.ToString();
            }

            current = current switch
            {
                InvocationExpressionSyntax inv => inv.Expression,
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null
            };
        }

        return null;
    }

    private static string? ExtractOwnedTypeName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
                return typeArg.ToString();
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveOwnedTypeFromConfiguration(
        InvocationExpressionSyntax invocation,
        CompilationModel compilationModel,
        INamedTypeSymbol? configuredEntityType,
        CancellationToken cancellationToken)
    {
        var explicitTypeName = ExtractOwnedTypeName(invocation);
        if (explicitTypeName != null)
            return compilationModel.FindTypeByName(explicitTypeName, cancellationToken);

        var entityType = configuredEntityType;
        if (entityType == null &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
            if (entityTypeName != null)
                entityType = compilationModel.FindTypeByName(entityTypeName, cancellationToken);
        }

        if (entityType == null)
            return null;

        var ownedNavigationName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
        if (ownedNavigationName == null)
            return null;

        return entityType.GetMembers(ownedNavigationName)
            .OfType<IPropertySymbol>()
            .Select(property => property.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();
    }
}
