using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private bool IsMissingPrimaryKey(
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities)
    {
        if (HasAttribute(entityType, "KeylessAttribute", "Microsoft.EntityFrameworkCore"))
            return false;
        if (keylessEntities.Contains(entityType))
            return false;

        if (HasAttribute(entityType, "OwnedAttribute", "Microsoft.EntityFrameworkCore"))
            return false;
        if (ownedEntities.Contains(entityType))
            return false;

        if (HasValidPrimaryKeyAttribute(entityType))
            return false;

        if (HasValidKeyProperty(entityType))
            return false;

        if (configuredEntities.Contains(entityType))
            return false;

        return true;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName, string namespaceName)
    {
        var shortName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName.Substring(0, attributeName.Length - 9)
            : attributeName;

        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass == null)
                continue;

            if (attr.AttributeClass.ContainingNamespace?.ToString() != namespaceName)
                continue;

            if (attr.AttributeClass.Name == attributeName || attr.AttributeClass.Name == shortName)
                return true;
        }

        return false;
    }

    private bool HasValidPrimaryKeyAttribute(INamedTypeSymbol entityType)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            if (attr.AttributeClass == null ||
                attr.AttributeClass.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore" ||
                attr.AttributeClass.Name is not ("PrimaryKeyAttribute" or "PrimaryKey"))
            {
                continue;
            }

            var propertyNames = GetPrimaryKeyPropertyNames(attr);
            if (propertyNames.Count == 0)
                return false;

            var allPropertiesValid = true;
            foreach (var propertyName in propertyNames)
            {
                if (!TryFindProperty(entityType, propertyName, out var prop) ||
                    !IsUsableKeyProperty(prop))
                {
                    allPropertiesValid = false;
                    break;
                }
            }

            if (allPropertiesValid)
                return true;
        }

        return false;
    }

    private static List<string> GetPrimaryKeyPropertyNames(AttributeData attr)
    {
        var propertyNames = new List<string>();
        foreach (var argument in attr.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var value in argument.Values)
                    if (value.Value is string propertyName)
                        propertyNames.Add(propertyName);
            }
            else if (argument.Value is string propertyName)
            {
                propertyNames.Add(propertyName);
            }
        }

        return propertyNames;
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
