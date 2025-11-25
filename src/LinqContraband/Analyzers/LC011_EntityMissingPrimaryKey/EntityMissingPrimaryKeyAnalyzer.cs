using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

/// <summary>
/// Analyzes Entity Framework Core entities to detect missing primary key definitions. Diagnostic ID: LC011
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Entities in EF Core require a primary key for change tracking, relationship management,
/// and identity resolution unless explicitly marked with [Keyless] or configured with HasNoKey(). Missing primary keys
/// can lead to runtime errors or unexpected behavior.</para>
/// <para><b>Detection methods:</b> Conventional keys (Id, EntityNameId), [Key] attribute, [PrimaryKey] attribute,
/// [Keyless] attribute, HasKey() fluent API, HasNoKey() fluent API, OwnsOne/OwnsMany (owned types don't need keys),
/// or IEntityTypeConfiguration implementations.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMissingPrimaryKeyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC011";
    private const string Category = "Design";
    private static readonly LocalizableString Title = "Design: Entity missing Primary Key";

    private static readonly LocalizableString MessageFormat =
        "Entity '{0}' does not have a primary key defined by convention (Id, {0}Id), attributes ([Key], [PrimaryKey]), [Keyless]/HasNoKey() opt-out, or Fluent API configuration";

    private static readonly LocalizableString Description =
        "Entities in EF Core require a Primary Key unless marked as [Keyless] or configured with HasNoKey(). Ensure the entity has a property named 'Id', '{EntityName}Id', a property decorated with [Key], or is configured via Fluent API.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
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

        // Must be a DbContext subclass (not the base class itself)
        if (!namedType.IsDbContext()) return;
        if (namedType.Name == "DbContext" &&
            namedType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            return;

        // Gather keyless and owned entities from OnModelCreating
        var keylessEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ownedEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        ScanOnModelCreating(namedType, keylessEntities, ownedEntities, context.Compilation);

        // Gather configured entities from IEntityTypeConfiguration<T>
        var configuredEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        ScanEntityTypeConfigurations(context.Compilation, configuredEntities, keylessEntities);

        // Find all DbSet<T> members (properties AND explicit fields, not backing fields)
        foreach (var member in namedType.GetMembers())
        {
            ITypeSymbol? dbSetType = null;
            Location? location = null;

            if (member is IPropertySymbol property)
            {
                dbSetType = property.Type;
                location = property.Locations.FirstOrDefault();
            }
            else if (member is IFieldSymbol field)
            {
                // Skip compiler-generated backing fields (they start with <)
                if (field.IsImplicitlyDeclared || field.Name.StartsWith("<"))
                    continue;

                dbSetType = field.Type;
                location = field.Locations.FirstOrDefault();
            }

            if (dbSetType is not INamedTypeSymbol propType || !propType.IsDbSet())
                continue;

            var entityType = propType.TypeArguments.Length > 0
                ? propType.TypeArguments[0] as INamedTypeSymbol
                : null;

            if (entityType == null || location == null) continue;

            // Check if entity is missing a primary key
            if (IsMissingPrimaryKey(entityType, namedType, configuredEntities, keylessEntities, ownedEntities, context.Compilation))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location, entityType.Name));
            }
        }
    }

    /// <summary>
    /// Scans OnModelCreating for HasNoKey() and OwnsOne/OwnsMany configurations.
    /// </summary>
    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities,
        Compilation compilation)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty) return;

        if (methods[0] is not IMethodSymbol onModelCreating) return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

                var methodName = memberAccess.Name.Identifier.Text;

                // Check for HasNoKey()
                if (methodName == "HasNoKey")
                {
                    var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
                    if (entityTypeName != null)
                    {
                        var entityType = FindTypeByName(compilation, entityTypeName);
                        if (entityType != null)
                            keylessEntities.Add(entityType);
                    }
                }

                // Check for OwnsOne/OwnsMany - extract the owned type
                if (methodName == "OwnsOne" || methodName == "OwnsMany")
                {
                    var ownedTypeName = ExtractOwnedTypeName(invocation);
                    if (ownedTypeName != null)
                    {
                        var ownedType = FindTypeByName(compilation, ownedTypeName);
                        if (ownedType != null)
                            ownedEntities.Add(ownedType);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Scans for IEntityTypeConfiguration implementations and caches configured entities.
    /// </summary>
    private static void ScanEntityTypeConfigurations(
        Compilation compilation,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities)
    {
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null) return;

        // Search all types in the compilation
        foreach (var type in GetAllTypes(compilation.GlobalNamespace))
        {
            if (type.AllInterfaces.IsEmpty) continue;

            foreach (var iface in type.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface))
                    continue;

                if (iface.TypeArguments.Length > 0 &&
                    iface.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    var (hasKey, hasNoKey) = CheckConfigureMethod(type);

                    if (hasKey)
                        configuredEntities.Add(entityType);

                    if (hasNoKey)
                        keylessEntities.Add(entityType);
                }
            }
        }
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

    private static (bool hasKey, bool hasNoKey) CheckConfigureMethod(INamedTypeSymbol configClass)
    {
        var configureMethod = configClass.GetMembers("Configure").FirstOrDefault() as IMethodSymbol;
        if (configureMethod == null) return (false, false);

        var hasKey = false;
        var hasNoKey = false;

        foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var methodName = memberAccess.Name.Identifier.Text;
                    if (methodName == "HasKey") hasKey = true;
                    if (methodName == "HasNoKey") hasNoKey = true;
                }
            }
        }

        return (hasKey, hasNoKey);
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

    private static INamedTypeSymbol? FindTypeByName(Compilation compilation, string typeName)
    {
        var type = compilation.GetTypeByMetadataName(typeName);
        if (type != null) return type;

        var simpleName = typeName.Contains('.') ? typeName.Substring(typeName.LastIndexOf('.') + 1) : typeName;
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
                    if (typeFullName.EndsWith(fullName, StringComparison.Ordinal) ||
                        fullName.EndsWith(type.Name, StringComparison.Ordinal))
                        return type;
                }
                else
                {
                    return type;
                }
            }

            foreach (var nested in type.GetTypeMembers())
            {
                if (nested.Name == simpleName)
                    return nested;
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            var found = FindTypeInNamespace(childNs, simpleName, fullName);
            if (found != null) return found;
        }

        return null;
    }

    private bool IsMissingPrimaryKey(
        INamedTypeSymbol entityType,
        INamedTypeSymbol dbContextType,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities,
        Compilation compilation)
    {
        // Check 1: Is entity marked as keyless?
        if (HasAttribute(entityType, "KeylessAttribute") || HasAttribute(entityType, "Keyless"))
            return false;
        if (keylessEntities.Contains(entityType))
            return false;

        // Check 2: Is entity an owned type?
        if (ownedEntities.Contains(entityType))
            return false;

        // Check 3: Has [PrimaryKey] attribute?
        if (HasAttribute(entityType, "PrimaryKeyAttribute") || HasAttribute(entityType, "PrimaryKey"))
            return false;

        // Check 4: Has valid key property by convention or [Key] attribute?
        if (HasValidKeyProperty(entityType))
            return false;

        // Check 5: Has fluent HasKey() in OnModelCreating?
        if (HasFluentKeyConfiguration(dbContextType, entityType, compilation))
            return false;

        // Check 6: Has IEntityTypeConfiguration<T> with HasKey()?
        if (configuredEntities.Contains(entityType))
            return false;

        return true;
    }

    private bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass == null) continue;
            if (attr.AttributeClass.Name == attributeName) return true;
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
                if (member is not IPropertySymbol prop) continue;

                // Check for [Key] attribute
                if (HasAttribute(prop, "KeyAttribute") || HasAttribute(prop, "Key"))
                {
                    if (IsPublicProperty(prop) && IsValidKeyType(prop.Type))
                        return true;
                }

                // Check for convention: Id or {EntityName}Id
                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsPublicProperty(prop) && IsValidKeyType(prop.Type))
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
        // Special types (int, long, string, etc.) are valid
        if (type.SpecialType != SpecialType.None)
            return true;

        // Enums are valid
        if (type.TypeKind == TypeKind.Enum)
            return true;

        // Struct types are generally valid (Guid, DateTime, custom value types)
        if (type.TypeKind == TypeKind.Struct)
            return true;

        // Nullable value types are valid
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;

        // Byte arrays are valid (for binary keys)
        if (type is IArrayTypeSymbol arrayType && arrayType.ElementType.SpecialType == SpecialType.System_Byte)
            return true;

        // Reference types that are NOT entities/navigation properties are invalid
        return false;
    }

    private bool HasFluentKeyConfiguration(INamedTypeSymbol dbContextType, INamedTypeSymbol entityType, Compilation compilation)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty) return false;

        if (methods[0] is not IMethodSymbol onModelCreating) return false;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

                var methodName = memberAccess.Name.Identifier.Text;

                if (methodName == "HasKey")
                {
                    var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
                    if (entityTypeName != null)
                    {
                        var matchedType = FindTypeByName(compilation, entityTypeName);
                        if (matchedType != null &&
                            SymbolEqualityComparer.Default.Equals(matchedType, entityType))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }
}
