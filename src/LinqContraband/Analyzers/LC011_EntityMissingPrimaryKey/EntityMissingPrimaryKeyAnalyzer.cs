using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMissingPrimaryKeyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC011";
    private const string Category = "Design";
    private static readonly LocalizableString Title = "Design: Entity missing Primary Key";

    private static readonly LocalizableString MessageFormat =
        "Entity '{0}' does not have a primary key defined by convention (Id, {0}Id), attributes ([Key], [PrimaryKey]), or [Keyless] opt-out";

    private static readonly LocalizableString Description =
        "Entities in EF Core require a Primary Key unless marked as [Keyless]. Ensure the entity has a property named 'Id', '{EntityName}Id', a property decorated with [Key], or is configured via Fluent API.";

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
        {
            if (member is IPropertySymbol property && 
                property.Type is INamedTypeSymbol propType && 
                IsDbSet(propType))
            {
                var entityType = propType.TypeArguments.Length > 0 ? propType.TypeArguments[0] as INamedTypeSymbol : null;
                if (entityType == null) continue;

                if (IsMissingPrimaryKey(entityType, namedType))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, property.Locations[0], entityType.Name));
                }
            }
        }
    }

    private bool IsMissingPrimaryKey(INamedTypeSymbol entityType, INamedTypeSymbol dbContextType)
    {
        // Heuristic 1: Attributes (Class Level)
        if (HasAttribute(entityType, "KeylessAttribute") || HasAttribute(entityType, "Keyless")) return false;
        if (HasAttribute(entityType, "PrimaryKeyAttribute") || HasAttribute(entityType, "PrimaryKey")) return false;

        // Heuristic 2: Properties (Hierarchy)
        if (HasKeyProperty(entityType)) return false;

        // Heuristic 3: Fluent API (Basic)
        if (HasFluentKeyConfiguration(dbContextType, entityType)) return false;

        return true;
    }

    private bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass == null) continue;
            if (attr.AttributeClass.Name == attributeName) return true;
            if (attributeName.EndsWith("Attribute") && attr.AttributeClass.Name == attributeName.Substring(0, attributeName.Length - 9)) return true;
        }
        return false;
    }

    private bool HasKeyProperty(INamedTypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop)
                {
                    // [Key] Attribute
                    if (HasAttribute(prop, "KeyAttribute") || HasAttribute(prop, "Key")) return true;

                    // Convention: Id
                    if (prop.Name.Equals("Id", System.StringComparison.OrdinalIgnoreCase)) return true;

                    // Convention: {EntityName}Id
                    if (prop.Name.Equals($"{entityType.Name}Id", System.StringComparison.OrdinalIgnoreCase)) return true;
                }
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

        // We need to access the syntax of OnModelCreating to find invocations.
        // Symbols alone don't give us the body operations easily without compilation access
        // inside a SymbolAction.
        // BUT, SymbolAnalysisContext doesn't give operation access directly for other methods.
        // We have to get syntax references.

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            // Simple textual/syntax check for now as full semantic analysis of the method body 
            // from a SymbolAction on the class is expensive/complex.
            // We'll look for invocations of HasKey containing our entity type name.
            
            // This is a heuristic approximation.
            var text = syntax.ToString();
            if (text.Contains($"Entity<{entityType.Name}>") && text.Contains("HasKey")) return true;
            
            // Also check for non-generic usage: Entity(typeof(T)) ?? tougher to parse stringly.
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
}

