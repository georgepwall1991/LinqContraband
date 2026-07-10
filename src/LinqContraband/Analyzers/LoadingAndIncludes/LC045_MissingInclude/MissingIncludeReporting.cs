using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static void ReportMissingIncludeDiagnostics(
        OperationAnalysisContext context,
        IOperation querySource,
        QueryChainInfo query,
        List<NavigationAccess> accesses)
    {
        // First access site per distinct missing path.
        var firstAccessByPath = new Dictionary<string, NavigationAccess>(System.StringComparer.Ordinal);
        foreach (var access in accesses)
        {
            if (query.IncludedPrefixes.Contains(access.Path))
                continue;

            if (!firstAccessByPath.TryGetValue(access.Path, out var existing) ||
                access.Syntax.SpanStart < existing.Syntax.SpanStart)
            {
                firstAccessByPath[access.Path] = access;
            }
        }

        if (firstAccessByPath.Count == 0)
            return;

        var querySourceLocation = new[] { querySource.Syntax.GetLocation() };

        foreach (var pair in firstAccessByPath.OrderBy(entry => entry.Value.Syntax.SpanStart))
        {
            // Only maximal paths: fixing "Customer.Address" eagerly loads "Customer" too.
            if (HasLongerMissingPath(firstAccessByPath, pair.Key))
                continue;

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(NavigationPathProperty, pair.Key);

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                pair.Value.Syntax.GetLocation(),
                additionalLocations: querySourceLocation,
                properties: properties,
                pair.Key,
                query.EntityType.Name));
        }
    }

    private static bool HasLongerMissingPath(
        Dictionary<string, NavigationAccess> missingPaths,
        string path)
    {
        foreach (var candidate in missingPaths.Keys)
        {
            if (candidate.Length > path.Length &&
                candidate.StartsWith(path, System.StringComparison.Ordinal) &&
                candidate[path.Length] == '.')
            {
                return true;
            }
        }

        return false;
    }
}
