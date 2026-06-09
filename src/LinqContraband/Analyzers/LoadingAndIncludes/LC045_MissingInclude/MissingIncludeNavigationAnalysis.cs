using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static HashSet<INamedTypeSymbol> CollectDbSetEntityTypes(INamedTypeSymbol contextType)
    {
        var entityTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        for (var current = contextType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (property.Type is not INamedTypeSymbol propertyType) continue;
                if (!propertyType.IsDbSet()) continue;
                if (propertyType.TypeArguments.Length > 0 && propertyType.TypeArguments[0] is INamedTypeSymbol entityType)
                    entityTypes.Add(entityType);
            }
        }

        return entityTypes;
    }

    /// <summary>
    /// A property is a navigation when its type (or its collection element type) has a DbSet on
    /// the same context. Owned and unmapped types have no DbSet, so EF loads them with the entity
    /// (or never) and Include does not apply — they are deliberately never classified.
    /// </summary>
    private static bool TryGetNavigationTarget(
        IPropertySymbol property,
        HashSet<INamedTypeSymbol> entityTypes,
        out INamedTypeSymbol targetEntity,
        out bool isCollection)
    {
        targetEntity = null!;
        isCollection = false;

        if (property.IsStatic || property.Parameters.Length > 0)
            return false;

        if (property.Type is INamedTypeSymbol referenceTarget && entityTypes.Contains(referenceTarget))
        {
            targetEntity = referenceTarget;
            return true;
        }

        if (IncludePathParser.TryGetCollectionElementType(property.Type, out var elementType) &&
            elementType is INamedTypeSymbol collectionTarget &&
            entityTypes.Contains(collectionTarget))
        {
            targetEntity = collectionTarget;
            isCollection = true;
            return true;
        }

        return false;
    }

    private static bool IsPropertyOfEntity(IPropertySymbol property, INamedTypeSymbol entityType)
    {
        for (var current = (ITypeSymbol?)entityType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, property.ContainingType))
                return true;
        }

        return false;
    }
}
