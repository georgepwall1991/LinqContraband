using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionFixer
{
    private static bool TryCreateProjectionFixContext(
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ProjectionFixContext fixContext)
    {
        fixContext = null!;

        if (!TryGetTargetVariable(invocation, semanticModel, cancellationToken, out var variableSymbol))
            return false;

        var entityType = GetEntityType(invocation, semanticModel);
        if (entityType == null)
            return false;

        var accessedProperties = FindAccessedProperties(root, variableSymbol!, entityType, semanticModel, cancellationToken);
        if (accessedProperties.Count == 0)
            return false;

        fixContext = new ProjectionFixContext(invocation, accessedProperties);
        return true;
    }

    private static bool TryGetTargetVariable(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ILocalSymbol? variableSymbol)
    {
        variableSymbol = null;

        var variableDeclarator = invocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (variableDeclarator == null)
            return false;

        if (variableDeclarator.Parent is not VariableDeclarationSyntax declaration || !declaration.Type.IsVar)
            return false;

        variableSymbol = semanticModel.GetDeclaredSymbol(variableDeclarator, cancellationToken) as ILocalSymbol;
        return variableSymbol != null;
    }

    private static ITypeSymbol? GetEntityType(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var current = invocation.Expression;

        while (current is MemberAccessExpressionSyntax memberAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
            var type = typeInfo.Type;

            if (type is INamedTypeSymbol namedType && type.IsDbSet() && namedType.TypeArguments.Length > 0)
                return namedType.TypeArguments[0];

            if (type is INamedTypeSymbol nt && nt.IsGenericType && nt.TypeArguments.Length > 0 && type.IsIQueryable())
            {
                current = memberAccess.Expression;
                if (current is InvocationExpressionSyntax prevInvocation)
                    current = prevInvocation.Expression;
                continue;
            }

            current = memberAccess.Expression;
        }

        if (current is MemberAccessExpressionSyntax directAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(directAccess);
            if (typeInfo.Type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
                return namedType.TypeArguments[0];
        }

        if (invocation.Expression is MemberAccessExpressionSyntax)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol?.ReturnType is INamedTypeSymbol returnType && returnType.TypeArguments.Length > 0)
                return returnType.TypeArguments[0];
        }

        return null;
    }

}
