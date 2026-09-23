using System;
using System.Collections.Immutable;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

public sealed partial class MissingQueryTagsAnalyzer
{
    /// <summary>Operators that run the query. Sync forms come from Queryable/Enumerable, async forms from EF Core.</summary>
    private static readonly ImmutableHashSet<string> TerminalMethods = ImmutableHashSet.Create(
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
        "Sum",
        "SumAsync",
        "Min",
        "MinAsync",
        "Max",
        "MaxAsync",
        "Average",
        "AverageAsync",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync");

    /// <summary>Queryable operators that turn into joins, grouping, or APPLY in SQL; they count twice.</summary>
    private static readonly ImmutableHashSet<string> HeavyQueryableOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Join",
        "GroupJoin",
        "LeftJoin",
        "RightJoin",
        "GroupBy",
        "SelectMany");

    /// <summary>Queryable operators that do not change the SQL shape.</summary>
    private static readonly ImmutableHashSet<string> ZeroWeightQueryableOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AsQueryable");

    private static readonly ImmutableHashSet<string> IncludeOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Include",
        "ThenInclude");

    /// <summary>EF Core query options: they change tracking, filters, or query splitting, not the query's shape.</summary>
    private static readonly ImmutableHashSet<string> QueryOptionOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "AsSplitQuery",
        "AsSingleQuery",
        "IgnoreQueryFilters",
        "IgnoreAutoIncludes");
}
