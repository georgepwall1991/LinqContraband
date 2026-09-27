using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

public static partial class RuleCatalog
{
    private static ImmutableArray<RuleCatalogEntry> CreateLC061ToLC075Entries()
    {
        return ImmutableArray.Create(
            new RuleCatalogEntry(
                id: "LC061",
                slug: "LC061_UncachedCompiledQuery",
                title: "Compiled query is not cached",
                category: "Performance",
                domain: "Query Shape & Translation",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "UncachedCompiledQueryAnalyzer",
                fixerTypeName: "UncachedCompiledQueryFixer",
                documentationPath: "docs/LC061_UncachedCompiledQuery.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC061_UncachedCompiledQuery/UncachedCompiledQuerySample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/QueryShapeAndTranslation/LC061_UncachedCompiledQuery",
                hasCodeFix: true,
                noCodeFixRationale: null
            )
        );
    }
}
