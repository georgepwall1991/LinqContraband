using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

public static partial class RuleCatalog
{
    private static ImmutableArray<RuleCatalogEntry> CreateLC061ToLC075Entries()
    {
        return ImmutableArray.Create(
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
