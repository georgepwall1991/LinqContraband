using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool TryCreateRedundantDiagnostic(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IOperation receiver,
        out Diagnostic diagnostic)
    {
        diagnostic = null!;
        if (!IsMaterializingMethod(invocation.TargetMethod) ||
            invocation.TargetMethod.Name == "AsEnumerable")
        {
            return false;
        }

        if (!TryResolveInlineMaterializationOrigin(receiver, out var previousMaterialization) ||
            previousMaterialization.MaterializerName == "AsEnumerable")
        {
            return false;
        }

        // The redundant fix removes the previous materializer. Dropping a set materializer would
        // lose de-duplication or a custom equality comparer, so it is not a safe redundant source.
        if (IsDeduplicatingSetMaterializer(previousMaterialization.MaterializerName))
        {
            return false;
        }

        // Dictionary/Lookup sources transform shape; reporting the trailing call as redundant would
        // hide that ToDictionary().ToList() and ToLookup().ToList() produce different element shapes.
        if (IsKeyedOrGroupedMaterializer(previousMaterialization.MaterializerName))
        {
            return false;
        }

        var properties = CreateProperties(
            RedundantDiagnosticKind,
            previousMaterialization.OriginKind,
            invocation.TargetMethod.Name,
            previousMaterialization.MaterializerName);

        if (CanOfferRemoveRedundantMaterializationFix(invocation.TargetMethod.Name, previousMaterialization))
        {
            properties = properties.SetItem(FixKindKey, RemoveRedundantMaterializationFixKind);
        }

        diagnostic = Diagnostic.Create(
            RedundantRule,
            invocation.Syntax.GetLocation(),
            properties,
            invocation.TargetMethod.Name,
            previousMaterialization.MaterializerName);
        return true;
    }
}
