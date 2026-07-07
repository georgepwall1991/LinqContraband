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

}
