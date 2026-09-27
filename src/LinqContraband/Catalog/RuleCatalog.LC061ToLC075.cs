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
            ),
            new RuleCatalogEntry(
                id: "LC062",
                slug: "LC062_BlockingEfAsyncCall",
                title: "Blocking on an EF Core async call",
                category: "Performance",
                domain: "Execution & Async",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "BlockingEfAsyncCallAnalyzer",
                fixerTypeName: "BlockingEfAsyncCallFixer",
                documentationPath: "docs/LC062_BlockingEfAsyncCall.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC062_BlockingEfAsyncCall/BlockingEfAsyncCallSample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/ExecutionAndAsync/LC062_BlockingEfAsyncCall",
                hasCodeFix: true,
                noCodeFixRationale: null
            ),
            new RuleCatalogEntry(
                id: "LC063",
                slug: "LC063_TransactionUnderRetryingStrategy",
                title: "User transaction under a retrying execution strategy",
                category: "Reliability",
                domain: "Execution & Async",
                severity: DiagnosticSeverity.Warning,
                analyzerTypeName: "TransactionUnderRetryingStrategyAnalyzer",
                fixerTypeName: "TransactionUnderRetryingStrategyFixer",
                documentationPath: "docs/LC063_TransactionUnderRetryingStrategy.md",
                samplePath: "samples/LinqContraband.Sample/Samples/LC063_TransactionUnderRetryingStrategy/TransactionUnderRetryingStrategySample.cs",
                analyzerSourcePath: "src/LinqContraband/Analyzers/ExecutionAndAsync/LC063_TransactionUnderRetryingStrategy",
                hasCodeFix: true,
                noCodeFixRationale: null
            )
        );
    }
}
