using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

/// <summary>
/// Detects reference navigation properties without explicit foreign key properties.
/// Shadow FK properties cause subtle performance issues and API ergonomics problems. Diagnostic ID: LC027
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingExplicitForeignKeyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC027";
    private const string Category = "Design";
    private static readonly LocalizableString Title = "Missing Explicit Foreign Key Property";

    private static readonly LocalizableString MessageFormat =
        "Navigation property '{0}' has no explicit foreign key property. Consider adding '{0}Id' for better performance and API ergonomics.";

    private static readonly LocalizableString Description =
        "Reference navigation properties without explicit FK properties cause EF Core to create shadow properties, leading to performance issues and inability to set FK without loading the navigation entity.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeDbContext, SymbolKind.NamedType);
    }

    private void AnalyzeDbContext(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        if (!namedType.IsDbContext()) return;
        if (namedType.Name == "DbContext" &&
            namedType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            return;

        // Collect all entity types from DbSet<T> properties
        var entityTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var member in namedType.GetMembers())
        {
            if (member is not IPropertySymbol property) continue;
            if (property.Type is not INamedTypeSymbol propType || !propType.IsDbSet()) continue;
            if (propType.TypeArguments.Length > 0 && propType.TypeArguments[0] is INamedTypeSymbol entityType)
                entityTypes.Add(entityType);
        }

        // For each entity type, check reference navigation properties
        foreach (var entityType in entityTypes)
        {
            CheckEntityForMissingForeignKeys(entityType, entityTypes, context);
        }
    }

    private static void CheckEntityForMissingForeignKeys(
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> allEntityTypes,
        SymbolAnalysisContext context)
    {
        foreach (var member in entityType.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.Type is not INamedTypeSymbol propType) continue;

            // Skip collection navigations
            if (IsCollectionType(propType)) continue;

            // Check if this property's type is an entity type (reference navigation)
            if (!allEntityTypes.Contains(propType)) continue;

            // Check if there's a matching FK property
            if (HasMatchingForeignKey(entityType, prop, propType)) continue;

            // Flag the navigation property
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
        INamedTypeSymbol navType)
    {
        // Check for [ForeignKey] attribute on the navigation property
        if (HasForeignKeyAttribute(navProperty)) return true;

        // Check all properties for matching FK by convention or [ForeignKey] pointing to this nav
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;

                // Check [ForeignKey] attribute pointing to this navigation
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

                // Convention: {NavPropertyName}Id or {NavTypeName}Id
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
