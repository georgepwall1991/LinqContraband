using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static HashSet<INamedTypeSymbol> CollectDbSetEntityTypes(INamedTypeSymbol dbContextType)
    {
        var entityTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var member in dbContextType.GetMembers())
        {
            if (member is not IPropertySymbol property) continue;
            if (property.Type is not INamedTypeSymbol propType) continue;
            if (!propType.IsDbSet()) continue;
            if (propType.TypeArguments.Length > 0 && propType.TypeArguments[0] is INamedTypeSymbol entityType)
                entityTypes.Add(entityType);
        }

        return entityTypes;
    }

    private static void CheckEntityForMissingForeignKeys(
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> allEntityTypes,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        SymbolAnalysisContext context)
    {
        foreach (var member in entityType.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.Type is not INamedTypeSymbol propType) continue;
            if (IsCollectionType(propType)) continue;
            if (!allEntityTypes.Contains(propType)) continue;
            if (HasMatchingForeignKey(entityType, prop, propType, ownedEntities, configuredForeignKeys)) continue;

            var location = prop.Locations.FirstOrDefault();
            if (location != null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location, prop.Name));
            }
        }
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Array) return true;
        if (type.SpecialType == SpecialType.System_String) return false;

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var ns = named.ContainingNamespace?.ToString();
            if (ns == "System.Collections.Generic")
            {
                return named.Name is "List" or "IList" or "ICollection" or "IEnumerable"
                    or "HashSet" or "ISet" or "IReadOnlyList" or "IReadOnlyCollection";
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IEnumerable" &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic")
                return true;
        }

        return false;
    }

    private static bool HasMatchingForeignKey(
        INamedTypeSymbol entityType,
        IPropertySymbol navProperty,
        INamedTypeSymbol navType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        if (ownedEntities.Contains(navType)) return true;
        if (configuredForeignKeys.Contains(GetNavigationConfigurationKey(entityType, navProperty.Name))) return true;
        if (HasForeignKeyAttribute(navProperty)) return true;

        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;

                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass?.Name is "ForeignKeyAttribute" or "ForeignKey")
                    {
                        if (attr.ConstructorArguments.Length > 0 &&
                            attr.ConstructorArguments[0].Value is string fkNavName &&
                            fkNavName == navProperty.Name)
                            return true;
                    }
                }

                if (prop.Name.Equals($"{navProperty.Name}Id", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals($"{navType.Name}Id", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool HasForeignKeyAttribute(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "ForeignKeyAttribute" or "ForeignKey")
                return true;
        }
        return false;
    }
}
