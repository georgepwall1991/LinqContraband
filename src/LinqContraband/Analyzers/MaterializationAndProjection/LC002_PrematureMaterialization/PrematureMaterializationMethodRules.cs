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
