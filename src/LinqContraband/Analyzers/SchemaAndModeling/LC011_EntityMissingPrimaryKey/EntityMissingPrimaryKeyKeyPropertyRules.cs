using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private bool HasValidKeyProperty(INamedTypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                    continue;

                if (HasAttribute(prop, "KeyAttribute", "System.ComponentModel.DataAnnotations") &&
                    IsUsableKeyProperty(prop))
                {
                    return true;
                }

                if ((prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                      prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) &&
                    IsUsableKeyProperty(prop))
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool TryFindProperty(INamedTypeSymbol entityType, string propertyName, out IPropertySymbol property)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol prop)
                {
                    property = prop;
                    return true;
                }
            }

            current = current.BaseType;
        }

        property = null!;
        return false;
    }

    private static bool IsUsableKeyProperty(IPropertySymbol prop)
    {
        return IsPublicProperty(prop) &&
               !HasAttribute(prop, "NotMappedAttribute", "System.ComponentModel.DataAnnotations.Schema") &&
               IsValidKeyType(prop.Type);
    }

    private static bool IsPublicProperty(IPropertySymbol prop)
    {
        return prop.DeclaredAccessibility == Accessibility.Public &&
               prop.GetMethod?.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsValidKeyType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Object)
            return false;

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
