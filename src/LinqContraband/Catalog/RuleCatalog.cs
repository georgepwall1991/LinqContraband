using System;
using System.Collections.Immutable;
using System.Linq;

namespace LinqContraband.Catalog;

public static partial class RuleCatalog
{
    public static ImmutableArray<RuleCatalogEntry> All { get; } = CreateLC001ToLC015Entries()
        .AddRange(CreateLC016ToLC030Entries())
        .AddRange(CreateLC031ToLC045Entries());

    public static RuleCatalogEntry GetById(string id)
    {
        foreach (var rule in All)
        {
            if (string.Equals(rule.Id, id, StringComparison.Ordinal))
                return rule;
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown LinqContraband rule id.");
    }

    public static ImmutableArray<RuleCatalogEntry> GetByDomain(string domain)
    {
        return All.Where(rule => string.Equals(rule.Domain, domain, StringComparison.Ordinal))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
