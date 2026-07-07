using System.Collections.Immutable;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private static readonly ImmutableHashSet<string> HazardousEnumerableMethods = ImmutableHashSet.Create(
        "Aggregate",
        "All",
        "Any",
        "Average",
        "Contains",
        "Count",
        "ElementAt",
        "ElementAtOrDefault",
        "First",
        "FirstOrDefault",
        "Last",
        "LastOrDefault",
        "LongCount",
        "Max",
        "MaxBy",
        "Min",
        "MinBy",
        "SequenceEqual",
        "Single",
        "SingleOrDefault",
        "Sum",
        "ToArray",
        "ToDictionary",
        "ToHashSet",
        "ToList",
        "ToLookup");

    private static readonly ImmutableHashSet<string> MaterializingCollectionTypes = ImmutableHashSet.Create(
        "HashSet",
        "LinkedList",
        "List",
        "Queue",
        "SortedSet",
        "Stack");
}
