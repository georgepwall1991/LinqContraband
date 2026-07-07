using System;
using System.Collections.Immutable;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static readonly ImmutableHashSet<string> AllowedQuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "Include",
        "ThenInclude",
        "IgnoreQueryFilters",
        "AsSplitQuery",
        "AsSingleQuery",
        "AsTracking",
        "IgnoreAutoIncludes",
        "TagWith",
        "TagWithCallSite"
    );

    private static readonly ImmutableHashSet<string> MaterializerSteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync"
    );
}
