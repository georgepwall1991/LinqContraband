using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static List<NavigationAccess>? CollectNavigationAccessesFromExecutableRoot(
        IOperation executableRoot,
        IInvocationOperation materializer,
        ILocalSymbol resultLocal,
        HashSet<ILocalSymbol> entityLocals,
        bool returnsCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        CancellationToken cancellationToken)
    {
        bool IsTrackedLocal(IOperation? operation)
        {
            return operation?.UnwrapConversions() is ILocalReferenceOperation localReference &&
                   (SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal) ||
                    entityLocals.Contains(localReference.Local));
        }

        bool IsEntitySource(IOperation? operation)
        {
            if (IsTrackedLocal(operation))
                return true;

            // Direct indexed access: orders[0].Customer without an intermediate local.
            return returnsCollection && operation != null && IsIndexedAccessOf(operation, resultLocal);
        }

        var accesses = new List<NavigationAccess>();
        var satisfiedPaths = new Dictionary<string, int>(System.StringComparer.Ordinal);

        foreach (var descendant in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
                case IReturnOperation returnOperation when
                    returnOperation.ReturnedValue != null &&
                    IsEntitySource(returnOperation.ReturnedValue):
                    return null;

                case IInvocationOperation call when call != materializer:
                    if (IsEntitySource(call.Instance))
                        return null;
                    foreach (var argument in call.Arguments)
                    {
                        // Hydrate(orders) and Hydrate(orders[0]) both hand the entity to a
                        // helper that could explicitly load the navigation.
                        if (IsEntitySource(argument.Value))
                            return null;
                    }

                    break;

                case IAnonymousFunctionOperation lambda when
                    LambdaReferencesTrackedLocal(lambda, resultLocal, entityLocals, cancellationToken):
                    return null;

                case ISimpleAssignmentOperation assignmentOperation when
                    IsEntitySource(assignmentOperation.Value) &&
                    assignmentOperation.Target.UnwrapConversions() is not ILocalReferenceOperation:
                    return null;

                case IPropertyReferenceOperation propertyReference:
                    if (IsInsideNameOf(propertyReference))
                        break;
                    if (!TryGetAccessPath(propertyReference, entityType, entityTypes, IsEntitySource, out var path))
                        break;

                    if (IsWriteTarget(propertyReference))
                    {
                        // o.Customer = c assigns an in-memory object, so reads of that path
                        // and below it after the assignment are backed regardless of Include.
                        var spanStart = propertyReference.Syntax.SpanStart;
                        if (!satisfiedPaths.TryGetValue(path, out var existingSpan) || spanStart < existingSpan)
                            satisfiedPaths[path] = spanStart;
                        break;
                    }

                    // o.Items.Add(x): mutating a tracked entity's collection does not read it.
                    // Only collection navigations qualify; order.Customer.Clear() still reads
                    // the Customer navigation.
                    if (IsCollectionNavigation(propertyReference, entityTypes) &&
                        IsCollectionMutatorReceiver(propertyReference))
                    {
                        break;
                    }

                    accesses.Add(new NavigationAccess(path, propertyReference.Syntax));
                    break;
            }
        }

        if (satisfiedPaths.Count > 0)
            accesses.RemoveAll(access => IsSatisfied(access, satisfiedPaths));

        return accesses;
    }
}
