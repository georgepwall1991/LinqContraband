using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

public static partial class RuleCatalog
{
    private static ImmutableArray<RuleCatalogEntry> CreateLC061ToLC075Entries()
    {
        return ImmutableArray.Create(
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
            )
        );
    }
}
