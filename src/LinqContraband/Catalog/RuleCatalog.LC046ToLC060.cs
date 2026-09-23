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
                noCodeFixRationale: "No safe automated rewrite: sequential awaits and separate contexts have different performance, lifetime, transaction, and consistency semantics."
            ),
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
                noCodeFixRationale: null
            ),
            new RuleCatalogEntry(
                id: "LC048",
                slug: "LC048_LostUpdateRisk",
                title: "Tracked update can overwrite a concurrent change",
                category: "Reliability",
                domain: "Change Tracking & Context Lifetime",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "LostUpdateRiskAnalyzer",
                fixerTypeName: null,
                documentationPath: "docs/LC048_LostUpdateRisk.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC048_LostUpdateRisk/LostUpdateRiskSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/ChangeTrackingAndContextLifetime/LC048_LostUpdateRisk",
                hasCodeFix: false,
                noCodeFixRationale: "No safe automated rewrite: concurrency tokens, atomic updates, and explicit transactions have different schema, retry, transaction, and behavioral semantics."
            ),
            new RuleCatalogEntry(
                id: "LC049",
                slug: "LC049_IncludeIgnoredByProjection",
                title: "Include is ignored by a Select projection",
                category: "Performance",
                domain: "Loading & Includes",
                severity: DiagnosticSeverity.Info,
                analyzerTypeName: "IncludeIgnoredByProjectionAnalyzer",
                fixerTypeName: "IncludeIgnoredByProjectionFixer",
                documentationPath: "docs/LC049_IncludeIgnoredByProjection.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC049_IncludeIgnoredByProjection/IncludeIgnoredByProjectionSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/LoadingAndIncludes/LC049_IncludeIgnoredByProjection",
                hasCodeFix: true,
                noCodeFixRationale: null
            ),
            new RuleCatalogEntry(
                id: "LC050",
                slug: "LC050_OrderByBeforeDistinct",
                title: "OrderBy before Distinct is discarded",
                category: "Correctness",
                domain: "Query Shape & Translation",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "OrderByBeforeDistinctAnalyzer",
                fixerTypeName: "OrderByBeforeDistinctFixer",
                documentationPath: "docs/LC050_OrderByBeforeDistinct.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC050_OrderByBeforeDistinct/OrderByBeforeDistinctSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/QueryShapeAndTranslation/LC050_OrderByBeforeDistinct",
                hasCodeFix: true,
                noCodeFixRationale: null
            ),
            new RuleCatalogEntry(
                id: "LC051",
                slug: "LC051_ToAsyncEnumerableOnQuery",
                title: "ToAsyncEnumerable() runs an EF Core query synchronously",
                category: "Performance",
                domain: "Execution & Async",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "ToAsyncEnumerableOnQueryAnalyzer",
                fixerTypeName: "ToAsyncEnumerableOnQueryFixer",
                documentationPath: "docs/LC051_ToAsyncEnumerableOnQuery.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC051_ToAsyncEnumerableOnQuery/ToAsyncEnumerableOnQuerySample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/ExecutionAndAsync/LC051_ToAsyncEnumerableOnQuery",
                hasCodeFix: true,
                noCodeFixRationale: null
            ),
            new RuleCatalogEntry(
                id: "LC052",
                slug: "LC052_NonDeterministicModelData",
                title: "Model data uses a value that changes on every run",
                category: "Reliability",
                domain: "Schema & Modeling",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "NonDeterministicModelDataAnalyzer",
                fixerTypeName: null,
                documentationPath: "docs/LC052_NonDeterministicModelData.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC052_NonDeterministicModelData/NonDeterministicModelDataSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/SchemaAndModeling/LC052_NonDeterministicModelData",
                hasCodeFix: false,
                noCodeFixRationale: "No safe automated rewrite: a seed value needs a fixed literal only the author can choose, and a database-side default needs provider-specific SQL (GETUTCDATE(), now(), CURRENT_TIMESTAMP)."
            ),
            new RuleCatalogEntry(
                id: "LC053",
                slug: "LC053_OverwrittenQueryFilter",
                title: "Global query filter silently replaced by another HasQueryFilter",
                category: "Security",
                domain: "Schema & Modeling",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "OverwrittenQueryFilterAnalyzer",
                fixerTypeName: "OverwrittenQueryFilterFixer",
                documentationPath: "docs/LC053_OverwrittenQueryFilter.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC053_OverwrittenQueryFilter/OverwrittenQueryFilterSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/SchemaAndModeling/LC053_OverwrittenQueryFilter",
                hasCodeFix: true,
                noCodeFixRationale: null
            )
        );
    }
}
