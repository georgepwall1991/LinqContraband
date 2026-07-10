using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static bool TryGetAccessPath(
        IPropertyReferenceOperation propertyReference,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        EntityOriginResolver tryResolveEntityOrigin,
        out string path,
        out EntityOrigin origin
    )
    {
        path = null!;
        origin = null!;

        if (!TryGetNavigationTarget(propertyReference.Property, entityTypes, out _, out _))
            return false;

        var instance = propertyReference.Instance?.UnwrapConversions();

        // order?.Customer: the instance is the conditional-access placeholder; resolve it to
        // the guarded receiver so idiomatic null-guarded reads are still recognized.
        if (instance is IConditionalAccessInstanceOperation)
            instance = ResolveConditionalAccessReceiver(propertyReference)?.UnwrapConversions();

        if (tryResolveEntityOrigin(instance, out origin))
        {
            var originEntityType = origin.EntityType ?? entityType;
            if (!IsPropertyOfEntity(propertyReference.Property, originEntityType))
                return false;

            path = CombineNavigationPath(origin.NavigationPrefix, propertyReference.Property.Name);
            return true;
        }

        // Nested access through a reference navigation: o.Customer.Address => "Customer.Address".
        if (
            instance is IPropertyReferenceOperation parentReference
            && TryGetAccessPath(
                parentReference,
                entityType,
                entityTypes,
                tryResolveEntityOrigin,
                out var parentPath,
                out origin
            )
        )
        {
            if (
                !TryGetNavigationTarget(
                    parentReference.Property,
                    entityTypes,
                    out var parentTarget,
                    out var parentIsCollection
                ) || parentIsCollection
            )
            {
                return false;
            }

            if (!IsPropertyOfEntity(propertyReference.Property, parentTarget))
                return false;

            path = parentPath + "." + propertyReference.Property.Name;
            return true;
        }

        // Parenthesized conditional regrouping, e.g. (order?.Customer)?.Address, resolves the
        // Address instance back to the whole inner conditional access rather than directly to
        // the Customer property. Re-enter through the terminal navigation property so the
        // deeper path is still reported as Customer.Address.
        if (
            instance is IConditionalAccessOperation conditionalInstance
            && FindConditionalAccessTerminalProperty(conditionalInstance)
                is IPropertyReferenceOperation conditionalParentReference
            && TryGetAccessPath(
                conditionalParentReference,
                entityType,
                entityTypes,
                tryResolveEntityOrigin,
                out var conditionalParentPath,
                out origin
            )
        )
        {
            if (
                !TryResolveNavigationTargetForPath(
                    entityType,
                    conditionalParentPath,
                    entityTypes,
                    out var conditionalParentTarget,
                    out var conditionalParentIsCollection
                ) || conditionalParentIsCollection
            )
            {
                return false;
            }

            if (!IsPropertyOfEntity(propertyReference.Property, conditionalParentTarget))
                return false;

            path = conditionalParentPath + "." + propertyReference.Property.Name;
            return true;
        }

        return false;
    }

    private static string CombineNavigationPath(string prefix, string segment)
    {
        return prefix.Length == 0 ? segment : prefix + "." + segment;
    }

    private static bool TryResolveNavigationTargetForPath(
        INamedTypeSymbol entityType,
        string path,
        HashSet<INamedTypeSymbol> entityTypes,
        out INamedTypeSymbol targetEntity,
        out bool isCollection
    )
    {
        targetEntity = null!;
        isCollection = false;
        var currentEntity = entityType;

        foreach (var segment in path.Split('.'))
        {
            var segmentProperty = FindEntityProperty(currentEntity, segment);

            if (
                segmentProperty == null
                || !TryGetNavigationTarget(
                    segmentProperty,
                    entityTypes,
                    out targetEntity,
                    out isCollection
                )
            )
            {
                return false;
            }

            if (isCollection)
                return true;

            currentEntity = targetEntity;
        }

        return targetEntity != null;
    }

    private static IPropertySymbol? FindEntityProperty(INamedTypeSymbol entityType, string name)
    {
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol property && IsPropertyOfEntity(property, entityType))
                    return property;
            }
        }

        return null;
    }

    private static bool IsCollectionNavigation(
        IPropertyReferenceOperation propertyReference,
        HashSet<INamedTypeSymbol> entityTypes
    )
    {
        return TryGetNavigationTarget(
                propertyReference.Property,
                entityTypes,
                out _,
                out var isCollection
            ) && isCollection;
    }
}
