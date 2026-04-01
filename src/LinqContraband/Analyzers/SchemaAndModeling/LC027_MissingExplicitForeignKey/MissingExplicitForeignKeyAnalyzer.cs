using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        var ownedEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var configuredForeignKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in namedType.GetMembers())
        {
            if (member is not IPropertySymbol property) continue;
            if (property.Type is not INamedTypeSymbol propType || !propType.IsDbSet()) continue;
            if (propType.TypeArguments.Length > 0 && propType.TypeArguments[0] is INamedTypeSymbol entityType)
                entityTypes.Add(entityType);
        }

        ScanOnModelCreating(namedType, context.Compilation, ownedEntities, configuredForeignKeys);
        ScanEntityTypeConfigurations(context.Compilation, ownedEntities, configuredForeignKeys);

        // For each entity type, check reference navigation properties
        foreach (var entityType in entityTypes)
        {
            CheckEntityForMissingForeignKeys(entityType, entityTypes, ownedEntities, configuredForeignKeys, context);
        }
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

            // Skip collection navigations
            if (IsCollectionType(propType)) continue;

            // Check if this property's type is an entity type (reference navigation)
            if (!allEntityTypes.Contains(propType)) continue;

            // Check if there's a matching FK property
            if (HasMatchingForeignKey(entityType, prop, propType, ownedEntities, configuredForeignKeys)) continue;

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
        INamedTypeSymbol navType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        if (ownedEntities.Contains(navType)) return true;
        if (configuredForeignKeys.Contains(GetNavigationConfigurationKey(entityType, navProperty.Name))) return true;

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

    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        Compilation compilation,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            ProcessConfigurationSyntax(syntax, compilation, null, ownedEntities, configuredForeignKeys);
        }
    }

    private static void ScanEntityTypeConfigurations(
        Compilation compilation,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return;

        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface) ||
                    iface.TypeArguments.Length == 0 ||
                    iface.TypeArguments[0] is not INamedTypeSymbol entityType)
                {
                    continue;
                }

                var configureMethod = type.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
                if (configureMethod == null)
                    continue;

                foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxRef.GetSyntax();
                    ProcessConfigurationSyntax(syntax, compilation, entityType, ownedEntities, configuredForeignKeys);
                }
            }
        }
    }

    private static void ProcessConfigurationSyntax(
        SyntaxNode syntax,
        Compilation compilation,
        INamedTypeSymbol? configuredEntityType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName is "OwnsOne" or "OwnsMany")
            {
                var resolvedOwnedType = ResolveOwnedTypeFromConfiguration(invocation, compilation, configuredEntityType);
                if (resolvedOwnedType != null)
                    ownedEntities.Add(resolvedOwnedType);
                continue;
            }

            if (methodName != "HasForeignKey")
                continue;

            var navName = ExtractNavigationNameFromChain(memberAccess.Expression);
            if (navName == null)
                continue;

            var entityType = configuredEntityType;
            if (entityType == null)
            {
                var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
                if (entityTypeName == null)
                    continue;

                entityType = FindTypeByName(compilation, entityTypeName);
            }

            if (entityType != null)
                configuredForeignKeys.Add(GetNavigationConfigurationKey(entityType, navName));
        }
    }

    private static string GetNavigationConfigurationKey(INamedTypeSymbol entityType, string navigationName)
    {
        return entityType.ToDisplayString() + "|" + navigationName;
    }

    private static string? ExtractNavigationNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName is "HasOne" or "HasMany" or "WithOne" or "WithMany")
                {
                    var navName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                    if (navName != null)
                        return navName;
                }

                current = memberAccess.Expression;
                continue;
            }

            current = current switch
            {
                MemberAccessExpressionSyntax nextMemberAccess => nextMemberAccess.Expression,
                _ => null
            };
        }

        return null;
    }

    private static string? ExtractNavigationNameFromArgument(ExpressionSyntax? argument)
    {
        return argument switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => ExtractMemberName(simpleLambda.Body),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => ExtractMemberName(parenthesizedLambda.Body),
            _ => null
        };
    }

    private static string? ExtractMemberName(SyntaxNode body)
    {
        return body switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private static void TryAddResolvedType(string? typeName, Compilation compilation, HashSet<INamedTypeSymbol> targetSet)
    {
        if (typeName == null) return;
        var resolved = FindTypeByName(compilation, typeName);
        if (resolved != null) targetSet.Add(resolved);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(childNs))
                yield return type;
        }
    }

    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "Entity")
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                    return typeArg.ToString();
            }

            current = current switch
            {
                InvocationExpressionSyntax inv => inv.Expression,
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null
            };
        }

        return null;
    }

    private static string? ExtractOwnedTypeName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
                return typeArg.ToString();
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveOwnedTypeFromConfiguration(
        InvocationExpressionSyntax invocation,
        Compilation compilation,
        INamedTypeSymbol? configuredEntityType)
    {
        var explicitTypeName = ExtractOwnedTypeName(invocation);
        if (explicitTypeName != null)
            return FindTypeByName(compilation, explicitTypeName);

        var entityType = configuredEntityType;
        if (entityType == null &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
            if (entityTypeName != null)
                entityType = FindTypeByName(compilation, entityTypeName);
        }

        if (entityType == null)
            return null;

        var ownedNavigationName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
        if (ownedNavigationName == null)
            return null;

        return entityType.GetMembers(ownedNavigationName)
            .OfType<IPropertySymbol>()
            .Select(property => property.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();
    }

    private static INamedTypeSymbol? FindTypeByName(Compilation compilation, string typeName)
    {
        var type = compilation.GetTypeByMetadataName(typeName);
        if (type != null) return type;

        var simpleName = typeName.Contains('.')
            ? typeName.Substring(typeName.LastIndexOf('.') + 1)
            : typeName;
        return FindTypeInNamespace(compilation.GlobalNamespace, simpleName, typeName);
    }

    private static INamedTypeSymbol? FindTypeInNamespace(INamespaceSymbol ns, string simpleName, string fullName)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (type.Name == simpleName)
            {
                if (fullName.Contains('.'))
                {
                    var typeFullName = type.ToDisplayString();
                    if (typeFullName.Equals(fullName, StringComparison.Ordinal))
                        return type;

                    if (typeFullName.EndsWith(fullName, StringComparison.Ordinal))
                    {
                        var prefixLength = typeFullName.Length - fullName.Length;
                        if (prefixLength == 0 || typeFullName[prefixLength - 1] == '.')
                            return type;
                    }
                }
                else
                {
                    return type;
                }
            }

            foreach (var nested in type.GetTypeMembers())
            {
                if (nested.Name != simpleName)
                    continue;

                if (fullName.Contains('.'))
                {
                    var nestedFullName = nested.ToDisplayString();
                    if (nestedFullName.EndsWith(fullName, StringComparison.Ordinal))
                        return nested;
                }
                else
                {
                    return nested;
                }
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            var found = FindTypeInNamespace(childNs, simpleName, fullName);
            if (found != null) return found;
        }

        return null;
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
