using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionAnalyzer
{
    internal static bool TryGetAssignedLocal(IInvocationOperation invocation, out ILocalSymbol local)
    {
        local = null!;

        var current = invocation.Parent;
        while (current != null)
        {
            if (current is IVariableDeclaratorOperation declarator)
            {
                local = declarator.Symbol;
                return true;
            }

            if (current is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localReference)
            {
                local = localReference.Local;
                return true;
            }

            if (current is IExpressionStatementOperation || current is IReturnOperation)
                return false;

            current = current.Parent;
        }

        return false;
    }

    internal static bool TryAnalyzeLocalUsage(IOperation executableRoot, ILocalSymbol local, out IPropertySymbol property)
    {
        property = null!;
        var properties = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (!ReferenceEquals(localReference.FindOwningExecutableRoot(), executableRoot))
                return false;

            if (!TryGetConsumedProperty(localReference, out var propertyReference))
                return false;

            if (!IsScalarLikeType(propertyReference.Property.Type))
                return false;

            properties.Add(propertyReference.Property);
        }

        if (properties.Count != 1)
            return false;

        property = properties.First();
        return true;
    }

    internal static bool HasNullConditionalPropertyUsage(
        IOperation executableRoot,
        ILocalSymbol local,
        IPropertySymbol property)
    {
        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (!TryGetConsumedProperty(localReference, out var propertyReference))
                continue;

            if (!SymbolEqualityComparer.Default.Equals(propertyReference.Property, property))
                continue;

            if (localReference.Parent is IConditionalAccessOperation)
                return true;
        }

        return false;
    }

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

    private static bool IsScalarLikeType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type.SpecialType != SpecialType.None)
            return true;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.TypeKind == TypeKind.Struct)
            return true;

        return type.Name == "String";
    }
}
