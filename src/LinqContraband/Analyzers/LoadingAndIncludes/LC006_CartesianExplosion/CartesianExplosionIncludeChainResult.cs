using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    private enum QuerySplittingMode
    {
        None,
        Split,
        Single
    }

    private sealed class IncludeChainAnalysis
    {
        private readonly List<IncludePath> includePaths = new();

        public QuerySplittingMode EffectiveQueryMode { get; set; }

        public void AddIncludePath(IncludePath path)
        {
            if (path.Segments.Length > 0)
                includePaths.Add(path);
        }

        public bool TryGetRiskySiblingCollections(out ImmutableArray<string> siblings)
        {
            var seenIncludePaths = new HashSet<string>(StringComparer.Ordinal);
            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var groupSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var path in includePaths)
            {
                if (!seenIncludePaths.Add(path.Key))
                    continue;

                var collectionParent = new List<string>();
                var referencePrefix = new List<string>();

                foreach (var segment in path.Segments)
                {
                    if (segment.IsCollection)
                    {
                        var parentKey = string.Join(".", collectionParent);
                        if (!groups.TryGetValue(parentKey, out var group))
                        {
                            group = new List<string>();
                            groups[parentKey] = group;
                            groupSets[parentKey] = new HashSet<string>(StringComparer.Ordinal);
                        }

                        var siblingName = referencePrefix.Count == 0
                            ? segment.Name
                            : string.Join(".", referencePrefix.Concat(new[] { segment.Name }));

                        if (groupSets[parentKey].Add(siblingName))
                            group.Add(siblingName);

                        collectionParent.Add(segment.Name);
                        referencePrefix.Clear();
                    }
                    else
                    {
                        referencePrefix.Add(segment.Name);
                    }
                }
            }

            foreach (var group in groups.Values)
            {
                if (group.Count > 1)
                {
                    siblings = group.ToImmutableArray();
                    return true;
                }
            }

            siblings = ImmutableArray<string>.Empty;
            return false;
        }
    }
}
