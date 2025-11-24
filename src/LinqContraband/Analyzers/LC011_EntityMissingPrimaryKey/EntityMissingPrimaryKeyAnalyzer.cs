using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

/// <summary>
/// Analyzes Entity Framework Core entities to detect missing primary key definitions. Diagnostic ID: LC011
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Entities in EF Core require a primary key for change tracking, relationship management,
/// and identity resolution unless explicitly marked with [Keyless]. Missing primary keys can lead to runtime errors or
/// unexpected behavior. This analyzer checks for conventional keys (Id, EntityNameId), [Key] attribute, [PrimaryKey] attribute,
/// Fluent API configuration, or IEntityTypeConfiguration implementations.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMissingPrimaryKeyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC011";
    private const string Category = "Design";
    private static readonly LocalizableString Title = "Design: Entity missing Primary Key";

    private static readonly LocalizableString MessageFormat =
        "Entity '{0}' does not have a primary key defined by convention (Id, {0}Id), attributes ([Key], [PrimaryKey]), [Keyless] opt-out, or IEntityTypeConfiguration<T>";

    private static readonly LocalizableString Description =
        "Entities in EF Core require a Primary Key unless marked as [Keyless]. Ensure the entity has a property named 'Id', '{EntityName}Id', a property decorated with [Key], is configured via Fluent API, or has an IEntityTypeConfiguration<T>.";

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

        // Must inherit from DbContext
        if (!IsDbContext(namedType)) return;

        // Find all DbSet<T> properties
        foreach (var member in namedType.GetMembers())
            if (member is IPropertySymbol property &&
                property.Type is INamedTypeSymbol propType &&
                IsDbSet(propType))
            {
                var entityType = propType.TypeArguments.Length > 0
                    ? propType.TypeArguments[0] as INamedTypeSymbol
                    : null;
                if (entityType == null) continue;

                if (IsMissingPrimaryKey(entityType, namedType, context.Compilation))
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, property.Locations[0], entityType.Name));
            }
    }

    private bool IsMissingPrimaryKey(INamedTypeSymbol entityType, INamedTypeSymbol dbContextType,
        Compilation compilation)
    {
        // Heuristic 1: Attributes (Class Level)
        if (HasAttribute(entityType, "KeylessAttribute") || HasAttribute(entityType, "Keyless")) return false;
        if (HasAttribute(entityType, "PrimaryKeyAttribute") || HasAttribute(entityType, "PrimaryKey")) return false;

        // Heuristic 2: Properties (Hierarchy)
        if (HasKeyProperty(entityType)) return false;

        // Heuristic 3: Fluent API (Basic)
        if (HasFluentKeyConfiguration(dbContextType, entityType)) return false;

        // Heuristic 4: IEntityTypeConfiguration<T>
        if (HasEntityTypeConfigurationKey(compilation, entityType)) return false;

        return true;
    }

    private bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass == null) continue;
            if (attr.AttributeClass.Name == attributeName) return true;
            if (attributeName.EndsWith("Attribute") &&
                attr.AttributeClass.Name == attributeName.Substring(0, attributeName.Length - 9)) return true;
        }

        return false;
    }

    private bool HasKeyProperty(INamedTypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol prop)
                {
                    // [Key] Attribute
                    if (HasAttribute(prop, "KeyAttribute") || HasAttribute(prop, "Key")) return true;

                    // Convention: Id
                    if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) return true;

                    // Convention: {EntityName}Id
                    if (prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) return true;
                }

            current = current.BaseType;
        }

        return false;
    }

    private bool HasFluentKeyConfiguration(INamedTypeSymbol dbContextType, INamedTypeSymbol entityType)
    {
        // Scan OnModelCreating method for .Entity<T>().HasKey(...)
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty) return false;

        var onModelCreating = methods[0] as IMethodSymbol;
        if (onModelCreating == null) return false;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var text = syntax.ToString();
            if (text.Contains($"Entity<{entityType.Name}>") && text.Contains("HasKey")) return true;
        }

        return false;
    }

    private bool HasEntityTypeConfigurationKey(Compilation compilation, INamedTypeSymbol entityType)
    {
        // Find IEntityTypeConfiguration<T> symbol
        var interfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (interfaceSymbol == null) return false;

        var genericInterface = interfaceSymbol.Construct(entityType);

        // Scan types in the compilation that implement this interface
        // Note: This is expensive on large solutions. We are visiting global namespace types.
        // Optimization: Only check types that end in "Configuration" or are in same namespace? 
        // For now, we do a broad search but limit depth or use a visitor if possible. 
        // Roslyn doesn't have a quick "FindImplementations" without a workspace.
        // We have to iterate symbols.

        // Visitor approach to find types
        var visitor = new TypeCollector();
        visitor.Visit(compilation.GlobalNamespace);

        foreach (var type in visitor.Types)
        foreach (var iface in type.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(iface, genericInterface))
                // Found a configuration class for this entity.
                // Now check its Configure method for HasKey.
                if (HasConfigureMethodKey(type))
                    return true;

        return false;
    }

    private bool HasConfigureMethodKey(INamedTypeSymbol configClass)
    {
        var configureMethod = configClass.GetMembers("Configure").FirstOrDefault() as IMethodSymbol;
        if (configureMethod == null) return false;

        foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var text = syntax.ToString();
            // Check for .HasKey(...) inside Configure method
            if (text.Contains("HasKey")) return true;
        }

        return false;
    }

    private bool IsDbContext(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private bool IsDbSet(INamedTypeSymbol type)
    {
        if (type.Name == "DbSet" &&
            type.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            return true;

        // Check base types just in case
        var current = type.BaseType;
        while (current != null)
        {
            if (current.Name == "DbSet" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private class TypeCollector : SymbolVisitor
    {
        public List<INamedTypeSymbol> Types { get; } = new();

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers()) member.Accept(this);
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            Types.Add(symbol);
            foreach (var member in symbol.GetTypeMembers()) member.Accept(this);
        }
    }
}
