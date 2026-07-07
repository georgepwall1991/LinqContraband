using System;
using System.Collections.Immutable;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

public sealed partial class MissingQueryTagsAnalyzer
{
    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Any",
        "AnyAsync",
        "All",
        "AllAsync",
        "Count",
        "CountAsync",
        "LongCount",
        "LongCountAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "Last",
        "LastAsync",
        "LastOrDefault",
        "LastOrDefaultAsync",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync");

    private static readonly ImmutableHashSet<string> QuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "Select",
        "SelectMany",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "GroupBy",
        "Join",
        "Include",
        "ThenInclude",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "AsSplitQuery",
        "AsSingleQuery",
        "IgnoreQueryFilters",
        "IgnoreAutoIncludes",
        "OfType",
        "TagWith",
        "TagWithCallSite");
}
