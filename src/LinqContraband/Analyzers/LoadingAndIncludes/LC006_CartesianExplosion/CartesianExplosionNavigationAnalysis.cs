using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    private static bool TryGetIncludedNavigation(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        out string navigationName)
    {
        navigationName = string.Empty;
        if (invocationSyntax.ArgumentList.Arguments.Count == 0)
            return false;

        var lambdaExpression = invocationSyntax.ArgumentList.Arguments[invocationSyntax.ArgumentList.Arguments.Count - 1].Expression;
        var lambdaBody = lambdaExpression switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Body,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.Body,
            _ => null
        };

        if (lambdaBody is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var propertySymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        var propertyType = propertySymbol?.Type ?? semanticModel.GetTypeInfo(memberAccess).Type;
        if (propertyType == null || !IsCollection(propertyType))
            return false;

        navigationName = memberAccess.Name.Identifier.Text;
        return true;
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;
        if (type.TypeKind == TypeKind.Array)
            return true;

        if (type is INamedTypeSymbol namedType)
        {
            var ns = namedType.ContainingNamespace?.ToString();
            if (ns == "System.Collections.Generic" && namedType.IsGenericType)
            {
                return namedType.Name is "List" or "IList" or "IEnumerable" or "ICollection"
                    or "HashSet" or "ISet" or "IReadOnlyList" or "IReadOnlyCollection";
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == "IEnumerable" && iface.IsGenericType &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic")
            {
                return true;
            }
        }

        return false;
    }
}
