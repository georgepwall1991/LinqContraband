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

            if (IsCollectionLocalOrCopyOfIt(instance, collectionLocal))
                return true;
        }

        if (unwrapped is IArrayElementReferenceOperation arrayElement)
        {
            var arrayReference = arrayElement.ArrayReference.UnwrapConversions();
            if (arrayReference is IConditionalAccessInstanceOperation)
                arrayReference = ResolveConditionalAccessReceiver(arrayElement)?.UnwrapConversions();

            if (IsCollectionLocalOrCopyOfIt(arrayReference, collectionLocal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The collection local itself, or an element-preserving view or copy chain that ends at it —
    /// `orders.ToList()[0]` indexes a different collection holding the same entity instances.
    /// The chain must end at the result local, so a query materializer, whose receiver is a
    /// DbSet, can never match.
    /// </summary>
    private static bool IsCollectionLocalOrCopyOfIt(IOperation? operation, ILocalSymbol collectionLocal)
    {
        var compilation = operation?.SemanticModel?.Compilation;

        while (operation != null)
        {
            if (operation is ILocalReferenceOperation localReference)
            {
                return SymbolEqualityComparer.Default.Equals(localReference.Local, collectionLocal);
            }

            if (compilation == null ||
                operation is not IInvocationOperation view ||
                !(IsElementPreservingInMemoryView(view, compilation) || IsSequenceCopy(view, compilation)))
            {
                return false;
            }

            operation = GetQuerySource(view)?.UnwrapConversions();
        }

        return false;
    }
}
