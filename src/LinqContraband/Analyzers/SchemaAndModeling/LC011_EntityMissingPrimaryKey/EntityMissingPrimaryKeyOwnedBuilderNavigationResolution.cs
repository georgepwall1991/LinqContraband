using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool TryGetOwnedEntityType(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol ownedEntity)
    {
        ownedEntity = null!;

        if (memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
            {
                var resolvedOwnedType = compilationModel.FindTypeByName(typeArg.ToString(), cancellationToken);
                if (resolvedOwnedType != null)
                {
                    ownedEntity = resolvedOwnedType;
                    return true;
                }
            }
        }

        if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var ownerEntity))
            return false;

        var lambda = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression as LambdaExpressionSyntax;
        if (lambda?.Body is not MemberAccessExpressionSyntax navigationAccess)
            return false;

        var navigationName = navigationAccess.Name.Identifier.ValueText;
        var navigation = ownerEntity.GetMembers(navigationName).OfType<IPropertySymbol>().FirstOrDefault();
        if (navigation?.Type is not INamedTypeSymbol navigationType)
            return false;

        ownedEntity = TryGetCollectionElementType(navigationType) ?? navigationType;
        return true;
    }

    private static INamedTypeSymbol? TryGetCollectionElementType(INamedTypeSymbol navigationType)
    {
        if (navigationType.SpecialType == SpecialType.System_String)
            return null;

        foreach (var iface in navigationType.AllInterfaces)
        {
            if (iface.Name == "IEnumerable" &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic" &&
                iface.TypeArguments.Length == 1 &&
                iface.TypeArguments[0] is INamedTypeSymbol elementType)
            {
                return elementType;
            }
        }

        return null;
    }
}
