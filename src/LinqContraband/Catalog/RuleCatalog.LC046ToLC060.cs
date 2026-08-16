using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

public static partial class RuleCatalog
{
    private static ImmutableArray<RuleCatalogEntry> CreateLC046ToLC060Entries()
    {
        return ImmutableArray.Create(
            new RuleCatalogEntry(
                id: "LC046",
                slug: "LC046_ConcurrentDbContextOperations",
                title: "Concurrent EF Core operations on the same DbContext",
                category: "Safety",
                domain: "Execution & Async",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "ConcurrentDbContextOperationsAnalyzer",
                fixerTypeName: null,
                documentationPath: "docs/LC046_ConcurrentDbContextOperations.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC046_ConcurrentDbContextOperations/ConcurrentDbContextOperationsSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/ExecutionAndAsync/LC046_ConcurrentDbContextOperations",
                hasCodeFix: false,
                noCodeFixRationale: "No safe automated rewrite: sequential awaits and separate contexts have different performance, lifetime, transaction, and consistency semantics."),
            new RuleCatalogEntry(
                id: "LC047",
                slug: "LC047_ExecuteDeleteBypassesTrackedDelete",
                title: "ExecuteDelete bypasses the tracked delete pipeline",
                category: "Safety",
                domain: "Bulk Operations & Set-Based Writes",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "ExecuteDeleteBypassesTrackedDeleteAnalyzer",
                fixerTypeName: "ExecuteDeleteBypassesTrackedDeleteFixer",
                documentationPath: "docs/LC047_ExecuteDeleteBypassesTrackedDelete.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC047_ExecuteDeleteBypassesTrackedDelete/ExecuteDeleteBypassesTrackedDeleteSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/BulkOperationsAndSetBasedWrites/LC047_ExecuteDeleteBypassesTrackedDelete",
                hasCodeFix: true,
                noCodeFixRationale: null));
    }
}
