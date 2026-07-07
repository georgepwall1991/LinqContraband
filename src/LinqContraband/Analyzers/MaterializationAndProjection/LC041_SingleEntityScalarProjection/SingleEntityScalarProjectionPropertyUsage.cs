using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionAnalyzer
{
    private static bool TryGetConsumedProperty(
        ILocalReferenceOperation localReference,
        out IPropertyReferenceOperation propertyReference)
    {
        propertyReference = null!;

        if (localReference.Parent is IPropertyReferenceOperation directPropertyReference)
        {
            if (!ReferenceEquals(directPropertyReference.Instance?.UnwrapConversions(), localReference))
                return false;

            if (!IsReadOnlyPropertyReference(directPropertyReference))
                return false;

            propertyReference = directPropertyReference;
            return true;
        }

        if (localReference.Parent is IConditionalAccessOperation conditionalAccess)
        {
            if (!ReferenceEquals(conditionalAccess.Operation?.UnwrapConversions(), localReference))
                return false;

            if (!TryGetConditionalAccessProperty(conditionalAccess.WhenNotNull, out var conditionalPropertyReference))
                return false;

            if (!IsReadOnlyPropertyReference(conditionalPropertyReference))
                return false;

            propertyReference = conditionalPropertyReference;
            return true;
        }

        return false;
    }

    private static bool TryGetConditionalAccessProperty(
        IOperation whenNotNull,
        out IPropertyReferenceOperation propertyReference)
    {
        propertyReference = null!;
        var current = whenNotNull.UnwrapConversions();

        while (current != null)
        {
            if (current is IInvocationOperation invocation)
            {
                current = invocation.GetInvocationReceiver()?.UnwrapConversions();
                continue;
            }

            if (current is not IPropertyReferenceOperation candidate)
                return false;

            if (candidate.Instance?.UnwrapConversions() is IConditionalAccessInstanceOperation)
            {
                propertyReference = candidate;
                return true;
            }

            current = candidate.Instance?.UnwrapConversions();
        }

        return false;
    }

    private static bool IsReadOnlyPropertyReference(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent switch
        {
            ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, propertyReference) => false,
            ICompoundAssignmentOperation compoundAssignment when ReferenceEquals(compoundAssignment.Target, propertyReference) => false,
            IIncrementOrDecrementOperation incrementOrDecrement when ReferenceEquals(incrementOrDecrement.Target, propertyReference) => false,
            _ => true,
        };
    }
}
