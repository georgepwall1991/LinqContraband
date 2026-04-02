using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private bool IsMissingPrimaryKey(
        INamedTypeSymbol entityType,
        INamedTypeSymbol dbContextType,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities,
        Compilation compilation)
    {
        if (HasAttribute(entityType, "KeylessAttribute") || HasAttribute(entityType, "Keyless"))
            return false;
        if (keylessEntities.Contains(entityType))
            return false;

        if (ownedEntities.Contains(entityType))
            return false;

        if (HasAttribute(entityType, "PrimaryKeyAttribute") || HasAttribute(entityType, "PrimaryKey"))
            return false;

        if (HasValidKeyProperty(entityType))
            return false;

        if (HasFluentKeyConfiguration(dbContextType, entityType, compilation))
            return false;

        if (configuredEntities.Contains(entityType))
            return false;

        return true;
    }

    private bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass == null)
                continue;
            if (attr.AttributeClass.Name == attributeName)
                return true;
            if (attributeName.EndsWith("Attribute", StringComparison.Ordinal) &&
                attr.AttributeClass.Name == attributeName.Substring(0, attributeName.Length - 9))
                return true;
        }

        return false;
    }

    private bool HasValidKeyProperty(INamedTypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                    continue;

                if ((HasAttribute(prop, "KeyAttribute") || HasAttribute(prop, "Key")) &&
                    IsPublicProperty(prop) &&
                    IsValidKeyType(prop.Type))
                {
                    return true;
                }

                if ((prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) &&
                    IsPublicProperty(prop) &&
                    IsValidKeyType(prop.Type))
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsPublicProperty(IPropertySymbol prop)
    {
        return prop.DeclaredAccessibility == Accessibility.Public &&
               prop.GetMethod?.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsValidKeyType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
            return true;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.TypeKind == TypeKind.Struct)
            return true;

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;

        if (type is IArrayTypeSymbol arrayType && arrayType.ElementType.SpecialType == SpecialType.System_Byte)
            return true;

        return false;
    }
}
