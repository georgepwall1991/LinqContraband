using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

/// <summary>
/// Detects reference navigation properties without explicit foreign key properties.
/// Shadow FK properties cause subtle performance issues and API ergonomics problems. Diagnostic ID: LC027
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingExplicitForeignKeyAnalyzer : DiagnosticAnalyzer
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

        var entityTypes = CollectDbSetEntityTypes(namedType);
        var ownedEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var configuredForeignKeys = new HashSet<string>(StringComparer.Ordinal);

        ScanOnModelCreating(namedType, context.Compilation, ownedEntities, configuredForeignKeys);
        ScanEntityTypeConfigurations(context.Compilation, ownedEntities, configuredForeignKeys);

        foreach (var entityType in entityTypes)
        {
            CheckEntityForMissingForeignKeys(entityType, entityTypes, ownedEntities, configuredForeignKeys, context);
        }
    }
}
