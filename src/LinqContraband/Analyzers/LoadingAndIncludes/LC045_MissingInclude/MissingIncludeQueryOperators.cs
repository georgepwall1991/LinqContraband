using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

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

    private static bool IsExactCollectionElementExtraction(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null || method.Parameters.Length == 0)
            return false;

        var frameworkEnumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (frameworkEnumerable == null ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, frameworkEnumerable) ||
            !SymbolEqualityComparer.Default.Equals(
                method.Parameters[0].Type.OriginalDefinition,
                compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T)))
        {
            return false;
        }

        return method.Name switch
        {
            "First" or "FirstOrDefault" or
            "Single" or "SingleOrDefault" or
            "Last" or "LastOrDefault" => method.Parameters.Length == 1,
            "ElementAt" or "ElementAtOrDefault" =>
                method.Parameters.Length == 2 &&
                method.Parameters[1].Type.SpecialType == SpecialType.System_Int32,
            _ => false,
        };
    }
}
