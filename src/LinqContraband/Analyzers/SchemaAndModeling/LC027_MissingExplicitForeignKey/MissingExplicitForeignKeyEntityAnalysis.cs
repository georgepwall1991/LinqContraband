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
            if (!IsMappedProperty(entityType, prop)) continue;
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

    /// <summary>
    /// EF Core skips <c>[NotMapped]</c> properties and computed ones such as <c>PlanData NextPlan =&gt; NewPlan ?? Plan</c>:
    /// a property without a setter is mapped only through a backing field it can find by convention.
    /// </summary>
    private static bool IsMappedProperty(INamedTypeSymbol entityType, IPropertySymbol property)
    {
        if (property.IsStatic || property.IsIndexer)
            return false;

        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "NotMappedAttribute" or "NotMapped")
                return false;
        }

        if (property.SetMethod != null)
            return true;

        var camelName = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
        for (var type = entityType; type != null; type = type.BaseType)
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property) ||
                    field.Name == "_" + camelName || field.Name == "_" + property.Name ||
                    field.Name == "m_" + camelName || field.Name == "m_" + property.Name ||
                    field.Name == camelName)
                {
                    return true;
                }
            }
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
        if (IsPrincipalOfOneToOne(entityType, navType)) return true;

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

    /// <summary>
    /// Jellyfin's <c>User.ProfileImage</c> with <c>ImageInfo.UserId</c>: when the related type carries a
    /// <c>{Entity}Id</c> key back to this entity, EF Core makes it the dependent of a one-to-one, and this side
    /// is the principal, which has no foreign key to add.
    /// </summary>
    private static bool IsPrincipalOfOneToOne(INamedTypeSymbol entityType, INamedTypeSymbol navType)
    {
        var inverseKeyName = entityType.Name + "Id";
        for (var current = navType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(inverseKeyName))
            {
                if (member is IPropertySymbol)
                    return true;
            }
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
