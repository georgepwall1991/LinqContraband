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

}
