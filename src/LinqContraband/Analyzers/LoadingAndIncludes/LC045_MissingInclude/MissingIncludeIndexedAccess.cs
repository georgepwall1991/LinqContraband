using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static bool IsIndexedAccessOf(IOperation operation, ILocalSymbol collectionLocal)
    {
        var unwrapped = operation.UnwrapConversions();

        // orders?[0]: the conditional access wraps the indexed access; the indexer sits on
        // the WhenNotNull side with the collection behind the placeholder. WhenNotNull
        // strictly descends, so the recursion is bounded by the nesting depth.
        if (unwrapped is IConditionalAccessOperation conditionalAccess)
            return IsIndexedAccessOf(conditionalAccess.WhenNotNull, collectionLocal);

        if (unwrapped is IPropertyReferenceOperation propertyReference && propertyReference.Arguments.Length > 0)
        {
            var instance = propertyReference.Instance?.UnwrapConversions();
            if (instance is IConditionalAccessInstanceOperation)
                instance = ResolveConditionalAccessReceiver(propertyReference)?.UnwrapConversions();

            if (instance is ILocalReferenceOperation localReference &&
                SymbolEqualityComparer.Default.Equals(localReference.Local, collectionLocal))
            {
                return true;
            }
        }

        if (unwrapped is IArrayElementReferenceOperation arrayElement)
        {
            var arrayReference = arrayElement.ArrayReference.UnwrapConversions();
            if (arrayReference is IConditionalAccessInstanceOperation)
                arrayReference = ResolveConditionalAccessReceiver(arrayElement)?.UnwrapConversions();

            if (arrayReference is ILocalReferenceOperation arrayLocal &&
                SymbolEqualityComparer.Default.Equals(arrayLocal.Local, collectionLocal))
            {
                return true;
            }
        }

        return false;
    }
}
