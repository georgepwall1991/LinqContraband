using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool IsMaterializingMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is not ("System.Linq" or "Microsoft.EntityFrameworkCore" or "System.Collections.Immutable")) return false;

        if (method.Name == "AsEnumerable") return true;

        return method.Name == "ToList" ||
               method.Name == "ToListAsync" ||
               method.Name == "ToArray" ||
               method.Name == "ToArrayAsync" ||
               method.Name == "ToDictionary" ||
               method.Name == "ToDictionaryAsync" ||
               method.Name == "ToHashSet" ||
               method.Name == "ToHashSetAsync" ||
               method.Name == "ToLookup" ||
               method.Name.StartsWith("ToImmutable", StringComparison.Ordinal);
    }

    private static bool IsDirectCollectionMaterializer(string methodName)
    {
        return methodName is
            "ToList" or
            "ToArray" or
            "ToHashSet" or
            "ToImmutableList" or
            "ToImmutableArray" or
            "ToImmutableHashSet";
    }

    // A materializer that removes duplicates. Collapsing one of these followed by a non-set
    // materializer (e.g. ToHashSet().ToList()) is NOT redundant: removing the set silently drops
    // de-duplication, which the trailing ToList/ToArray would not restore.
    private static bool IsDeduplicatingSetMaterializer(string methodName)
    {
        return methodName is
            "ToHashSet" or
            "ToHashSetAsync" or
            "ToImmutableHashSet" or
            "ToImmutableSortedSet";
    }

    // A keyed (Dictionary) or grouped (Lookup) materializer produces a structure whose element type
    // differs from the source sequence, so a trailing materializer transforms its shape rather than
    // re-materialising the same sequence: ToDictionary().ToList() yields List<KeyValuePair<,>> and
    // ToLookup().ToList() yields List<IGrouping<,>>. The trailing call is therefore never redundant,
    // and removing the keyed/grouped source would change the result type - so such a source is not
    // treated as a redundant materialization (the "redundant" message would be misleading).
    private static bool IsKeyedOrGroupedMaterializer(string methodName)
    {
        return methodName is
            "ToDictionary" or
            "ToDictionaryAsync" or
            "ToLookup";
    }

    private static bool IsMaterializingConstructor(IMethodSymbol constructor)
    {
        var type = constructor.ContainingType;
        if (type.ContainingNamespace?.ToString() != "System.Collections.Generic") return false;

        return type.Name == "List" ||
               type.Name == "HashSet" ||
               type.Name == "Dictionary" ||
               type.Name == "SortedDictionary" ||
               type.Name == "SortedList" ||
               type.Name == "LinkedList" ||
               type.Name == "Queue" ||
               type.Name == "Stack";
    }
}
