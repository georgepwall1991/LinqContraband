using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Operators that keep the query shaped as "a stream of the root entity". Anything else
    /// (Select, Join, GroupBy, custom extensions, ...) reshapes the result or may add its own
    /// loading behaviour, so the whole query is conservatively out of scope.
    /// </summary>
    private static readonly ImmutableHashSet<string> ShapePreservingOperators = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "AsSplitQuery",
        "AsSingleQuery",
        "TagWith",
        "TagWithCallSite",
        "IgnoreQueryFilters",
        "Include",
        "ThenInclude");

    private static bool IsEntityMaterializer(IMethodSymbol method, out bool returnsCollection)
    {
        returnsCollection = false;

        switch (method.Name)
        {
            case "ToList":
            case "ToListAsync":
            case "ToArray":
            case "ToArrayAsync":
                returnsCollection = true;
                break;

            case "First":
            case "FirstAsync":
            case "FirstOrDefault":
            case "FirstOrDefaultAsync":
            case "Single":
            case "SingleAsync":
            case "SingleOrDefault":
            case "SingleOrDefaultAsync":
            case "Last":
            case "LastAsync":
            case "LastOrDefault":
            case "LastOrDefaultAsync":
                break;

            default:
                return false;
        }

        return method.IsFrameworkMethod();
    }
}
