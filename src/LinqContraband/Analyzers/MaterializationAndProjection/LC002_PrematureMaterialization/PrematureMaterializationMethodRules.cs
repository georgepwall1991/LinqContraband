using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool IsApprovedContinuationMethod(IMethodSymbol method, out bool returnsSequence)
    {
        returnsSequence = false;

        if (method.ContainingType.Name != "Enumerable" ||
            method.ContainingNamespace?.ToString() != "System.Linq")
        {
            return false;
        }

        if (HasUnsupportedComparerOverload(method) || HasUnsupportedIndexAwareLambda(method))
        {
            return false;
        }

        if (SequenceContinuationMethods.Contains(method.Name))
        {
            returnsSequence = true;
            return true;
        }

        if (TerminalContinuationMethods.Contains(method.Name))
        {
            return true;
        }

        return false;
    }

    private static bool HasUnsupportedComparerOverload(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return false;

        var lastParameterType = method.Parameters[method.Parameters.Length - 1].Type;
        return lastParameterType.Name is "IEqualityComparer" or "IComparer" &&
               lastParameterType.ContainingNamespace?.ToString() == "System.Collections.Generic";
    }

    private static bool HasUnsupportedIndexAwareLambda(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return false;

        var lastParameterType = method.Parameters[method.Parameters.Length - 1].Type as INamedTypeSymbol;
        var invokeMethod = lastParameterType?.DelegateInvokeMethod;
        return invokeMethod != null && invokeMethod.Parameters.Length > 1;
    }

    private static bool CanOfferMoveBeforeMaterializationFix(IMethodSymbol continuationMethod, MaterializationOrigin origin)
    {
        if (origin.OriginKind != InlineInvocationOriginKind) return false;
        if (!IsApprovedContinuationMethod(continuationMethod, out _)) return false;
        if (continuationMethod.Name is "Last" or "LastOrDefault") return false;

        return origin.MaterializerName is
            "ToList" or
            "ToArray" or
            "AsEnumerable" or
            "ToImmutableList" or
            "ToImmutableArray";
    }

    private static bool CanOfferRemoveRedundantMaterializationFix(string currentMaterializer, MaterializationOrigin previousMaterialization)
    {
        if (previousMaterialization.OriginKind != InlineInvocationOriginKind) return false;

        return currentMaterializer == "AsEnumerable" ||
               previousMaterialization.MaterializerName == "AsEnumerable" ||
               (IsDirectCollectionMaterializer(currentMaterializer) &&
                IsDirectCollectionMaterializer(previousMaterialization.MaterializerName));
    }

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
    // and removing the keyed/grouped source would change the result type — so such a source is not
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
