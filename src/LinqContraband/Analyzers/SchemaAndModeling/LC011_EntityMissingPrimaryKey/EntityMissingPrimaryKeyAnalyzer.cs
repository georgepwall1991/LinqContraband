using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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
public sealed partial class EntityMissingPrimaryKeyAnalyzer : DiagnosticAnalyzer
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

        if (!namedType.IsDbContext())
            return;
        if (namedType.Name == "DbContext" &&
            namedType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            return;

        var keylessEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ownedEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var configuredEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        ScanOnModelCreating(namedType, configuredEntities, keylessEntities, ownedEntities, context.Compilation);

        foreach (var member in namedType.GetMembers())
        {
            if (!TryGetDbSetMember(member, out var entityType, out var location))
                continue;

            if (IsMissingPrimaryKey(entityType!, configuredEntities, keylessEntities, ownedEntities))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location!, entityType!.Name));
            }
        }
    }

    private static bool TryGetDbSetMember(ISymbol member, out INamedTypeSymbol? entityType, out Location? location)
    {
        entityType = null;
        location = null;

        ITypeSymbol? dbSetType = null;
        switch (member)
        {
            case IPropertySymbol property:
                dbSetType = property.Type;
                location = property.Locations.FirstOrDefault();
                break;

            case IFieldSymbol field:
                if (field.IsImplicitlyDeclared || field.Name.StartsWith("<", StringComparison.Ordinal))
                    return false;

                dbSetType = field.Type;
                location = field.Locations.FirstOrDefault();
                break;
        }

        if (dbSetType is not INamedTypeSymbol namedType || !namedType.IsDbSet())
            return false;

        entityType = namedType.TypeArguments.Length > 0
            ? namedType.TypeArguments[0] as INamedTypeSymbol
            : null;

        return entityType != null && location != null;
    }
}
