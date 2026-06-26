using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static readonly ImmutableHashSet<string> CollectionMutatorMethods = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "Add",
        "AddRange",
        "Remove",
        "RemoveRange",
        "Clear");

    private readonly struct NavigationAccess
    {
        public NavigationAccess(string path, SyntaxNode syntax)
        {
            Path = path;
            Syntax = syntax;
        }

        public string Path { get; }
        public SyntaxNode Syntax { get; }
    }

    /// <summary>
    /// Collects every navigation path read from the materialized result inside the owning
    /// method. Returns null when the result (or an entity drawn from it) escapes — returned,
    /// passed as an argument, captured by a lambda, or stored outside a local — because a
    /// helper might explicitly load the navigation.
    /// </summary>
    private static List<NavigationAccess>? CollectNavigationAccesses(
        IInvocationOperation materializer,
        bool returnsCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        CancellationToken cancellationToken)
    {
        var upwardParent = WalkUpThroughWrappers(materializer.Parent);
        if (upwardParent is IPropertyReferenceOperation)
        {
            // Inline access like db.Orders.First().Customer.Name — only provable for
            // single-entity materializers.
            return returnsCollection
                ? null
                : CollectInlineAccesses(WalkUpThroughWrappers(materializer.Parent), entityType, entityTypes);
        }

        // db.Orders.FirstOrDefault()?.Customer.Name — the materializer is the guarded
        // receiver of a conditional access; the nav chain lives in WhenNotNull.
        if (upwardParent is IConditionalAccessOperation conditionalParent &&
            conditionalParent.Operation.UnwrapConversions() == materializer)
        {
            return returnsCollection
                ? null
                : CollectInlineAccesses(FindConditionalAccessEntryProperty(conditionalParent), entityType, entityTypes);
        }

        var resultLocal = FindVariableAssignment(materializer);
        if (resultLocal == null)
            return null;

        var executableRoot = materializer.FindOwningExecutableRoot();
        if (executableRoot == null)
            return null;

        // A reassigned result local could point at anything by the time it is read.
        if (LocalAssignmentCache.GetAssignments(executableRoot, resultLocal, cancellationToken).Count != 1)
            return null;

        var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        if (!returnsCollection)
            entityLocals.Add(resultLocal);

        if (returnsCollection)
        {
            foreach (var descendant in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (descendant)
                {
                    case IForEachLoopOperation forEach when
                        forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                        SymbolEqualityComparer.Default.Equals(collectionRef.Local, resultLocal):
                        foreach (var loopLocal in forEach.Locals)
                            entityLocals.Add(loopLocal);
                        break;

                    // A local fed from the result by index only counts while it can't be
                    // repointed at some unrelated object: require a single assignment.
                    case IVariableDeclaratorOperation declarator when
                        declarator.Initializer != null &&
                        IsIndexedAccessOf(declarator.Initializer.Value, resultLocal) &&
                        LocalAssignmentCache.GetAssignments(executableRoot, declarator.Symbol, cancellationToken).Count == 1:
                        entityLocals.Add(declarator.Symbol);
                        break;

                    case ISimpleAssignmentOperation assignmentOperation when
                        assignmentOperation.Target is ILocalReferenceOperation targetLocal &&
                        IsIndexedAccessOf(assignmentOperation.Value, resultLocal) &&
                        LocalAssignmentCache.GetAssignments(executableRoot, targetLocal.Local, cancellationToken).Count == 1:
                        entityLocals.Add(targetLocal.Local);
                        break;
                }
            }
        }

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
                        // (and below it) AFTER the assignment are backed regardless of
                        // Include. Reads before it still flag.
                        var spanStart = propertyReference.Syntax.SpanStart;
                        if (!satisfiedPaths.TryGetValue(path, out var existingSpan) || spanStart < existingSpan)
                            satisfiedPaths[path] = spanStart;
                        break;
                    }

                    // o.Items.Add(x): mutating a tracked entity's collection does not read it.
                    // Only collection navigations qualify — order.Customer.Clear() still reads
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

    private static bool IsSatisfied(NavigationAccess access, Dictionary<string, int> satisfiedPaths)
    {
        foreach (var pair in satisfiedPaths)
        {
            // Lexical ordering is the v1 heuristic: only reads after the assignment count
            // as backed (a conditional assignment before a read is conservatively quiet).
            if (access.Syntax.SpanStart < pair.Value)
                continue;

            var satisfied = pair.Key;
            if (access.Path.Length == satisfied.Length && access.Path == satisfied)
                return true;

            if (access.Path.Length > satisfied.Length &&
                access.Path[satisfied.Length] == '.' &&
                access.Path.StartsWith(satisfied, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<NavigationAccess> CollectInlineAccesses(
        IOperation? start,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes)
    {
        var accesses = new List<NavigationAccess>();
        var current = start;
        var currentEntity = entityType;
        string? path = null;

        while (current is IPropertyReferenceOperation propertyReference &&
               IsPropertyOfEntity(propertyReference.Property, currentEntity) &&
               TryGetNavigationTarget(propertyReference.Property, entityTypes, out var target, out var isCollection))
        {
            if (IsWriteTarget(propertyReference) ||
                (isCollection && IsCollectionMutatorReceiver(propertyReference)))
            {
                break;
            }

            path = path == null ? propertyReference.Property.Name : path + "." + propertyReference.Property.Name;
            accesses.Add(new NavigationAccess(path, propertyReference.Syntax));

            if (isCollection)
                break;

            currentEntity = target;
            current = WalkUpThroughWrappers(propertyReference.Parent);

            // ...?.Address continues the chain on the conditional access's WhenNotNull side —
            // but only when this property is the access's guarded receiver (Operation side).
            // Following a WhenNotNull-side parent would revisit this same property forever.
            if (current is IConditionalAccessOperation chainedAccess &&
                chainedAccess.Operation.UnwrapConversions() == propertyReference)
            {
                current = FindConditionalAccessEntryProperty(chainedAccess);
                continue;
            }

            if (current is IConditionalAccessOperation completedConditionalAccess &&
                TryFindRegroupedConditionalContinuation(completedConditionalAccess, out var continuation))
            {
                current = continuation;
            }
        }

        return accesses;
    }

    private static bool TryGetAccessPath(
        IPropertyReferenceOperation propertyReference,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        System.Func<IOperation?, bool> isEntitySource,
        out string path)
    {
        path = null!;

        if (!TryGetNavigationTarget(propertyReference.Property, entityTypes, out _, out _))
            return false;

        var instance = propertyReference.Instance?.UnwrapConversions();

        // order?.Customer: the instance is the conditional-access placeholder; resolve it to
        // the guarded receiver so idiomatic null-guarded reads are still recognized.
        if (instance is IConditionalAccessInstanceOperation)
            instance = ResolveConditionalAccessReceiver(propertyReference)?.UnwrapConversions();

        if (isEntitySource(instance))
        {
            if (!IsPropertyOfEntity(propertyReference.Property, entityType))
                return false;

            path = propertyReference.Property.Name;
            return true;
        }

        // Nested access through a reference navigation: o.Customer.Address => "Customer.Address".
        if (instance is IPropertyReferenceOperation parentReference &&
            TryGetAccessPath(parentReference, entityType, entityTypes, isEntitySource, out var parentPath))
        {
            if (!TryGetNavigationTarget(parentReference.Property, entityTypes, out var parentTarget, out var parentIsCollection) ||
                parentIsCollection)
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
        if (instance is IConditionalAccessOperation conditionalInstance &&
            FindConditionalAccessTerminalProperty(conditionalInstance) is IPropertyReferenceOperation conditionalParentReference &&
            TryGetAccessPath(conditionalParentReference, entityType, entityTypes, isEntitySource, out var conditionalParentPath))
        {
            if (!TryResolveNavigationTargetForPath(entityType, conditionalParentPath, entityTypes, out var conditionalParentTarget, out var conditionalParentIsCollection) ||
                conditionalParentIsCollection)
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

    private static bool TryResolveNavigationTargetForPath(
        INamedTypeSymbol entityType,
        string path,
        HashSet<INamedTypeSymbol> entityTypes,
        out INamedTypeSymbol targetEntity,
        out bool isCollection)
    {
        targetEntity = null!;
        isCollection = false;
        var currentEntity = entityType;

        foreach (var segment in path.Split('.'))
        {
            var segmentProperty = FindEntityProperty(currentEntity, segment);

            if (segmentProperty == null ||
                !TryGetNavigationTarget(segmentProperty, entityTypes, out targetEntity, out isCollection))
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

    private static IPropertyReferenceOperation? FindConditionalAccessTerminalProperty(IConditionalAccessOperation conditionalAccess)
    {
        var current = conditionalAccess.WhenNotNull.UnwrapConversions();

        while (true)
        {
            if (current is IConditionalAccessOperation nested)
            {
                current = nested.WhenNotNull.UnwrapConversions();
                continue;
            }

            if (current is IInvocationOperation)
            {
                return null;
            }

            return current as IPropertyReferenceOperation;
        }
    }

    private static bool TryFindRegroupedConditionalContinuation(
        IConditionalAccessOperation completedConditionalAccess,
        out IOperation? continuation)
    {
        continuation = null;

        for (var parent = completedConditionalAccess.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is IConversionOperation or IParenthesizedOperation)
                continue;

            if (parent is IConditionalAccessOperation regroupedConditionalAccess &&
                UnwrapOperandWrappers(regroupedConditionalAccess.Operation) == completedConditionalAccess)
            {
                continuation = FindConditionalAccessEntryProperty(regroupedConditionalAccess);
                return continuation != null;
            }

            return false;
        }

        return false;
    }

    private static IOperation UnwrapOperandWrappers(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;

                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;

                default:
                    return operation;
            }
        }
    }

    private static bool IsCollectionNavigation(
        IPropertyReferenceOperation propertyReference,
        HashSet<INamedTypeSymbol> entityTypes)
    {
        return TryGetNavigationTarget(propertyReference.Property, entityTypes, out _, out var isCollection) &&
               isCollection;
    }

    /// <summary>
    /// For db.Orders.First()?.Customer.Name the navigation chain hangs off WhenNotNull;
    /// descend it to the property whose instance is the conditional-access placeholder.
    /// </summary>
    private static IOperation? FindConditionalAccessEntryProperty(IConditionalAccessOperation conditionalAccess)
    {
        IOperation? current = conditionalAccess.WhenNotNull.UnwrapConversions();

        while (true)
        {
            // X()?.Customer?.Name nests another conditional access in WhenNotNull; the entry
            // property lives on the nested access's Operation side. Descending Operation
            // sides strictly shrinks the tree, so this cannot revisit a node.
            if (current is IConditionalAccessOperation nested)
            {
                current = nested.Operation.UnwrapConversions();
                continue;
            }

            // X()?.Customer.Clear(): the arm is an invocation whose instance chain holds the
            // navigation; descend into the receiver.
            if (current is IInvocationOperation invocation)
            {
                current = invocation.Instance?.UnwrapConversions();
                continue;
            }

            if (current is not IPropertyReferenceOperation propertyReference)
                return null;

            var instance = propertyReference.Instance?.UnwrapConversions();
            if (instance is IConditionalAccessInstanceOperation)
                return propertyReference;

            current = instance;
        }
    }

    private static IOperation? ResolveConditionalAccessReceiver(IOperation operation)
    {
        // o?.Customer?.Name nests two conditional accesses, and Customer's placeholder belongs
        // to the OUTER one: its owner is the nearest ancestor reached from the WhenNotNull
        // side. Matching the first IConditionalAccessOperation ancestor regardless of side
        // returns the inner access — whose Operation is the very property reference being
        // resolved — and TryGetAccessPath then recurses on its own input until the stack
        // overflows, killing the whole compilation.
        var child = operation;
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IConditionalAccessOperation conditionalAccess &&
                conditionalAccess.WhenNotNull == child)
            {
                return conditionalAccess.Operation;
            }

            child = current;
        }

        return null;
    }

    private static bool IsInsideNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is INameOfOperation)
                return true;
            if (current is IBlockOperation or IExpressionStatementOperation)
                return false;
        }

        return false;
    }

    private static bool IsWriteTarget(IPropertyReferenceOperation propertyReference)
    {
        // o.Customer = c (also += / ??=): relationship fix-up, not a read of unloaded data.
        if (propertyReference.Parent is IAssignmentOperation assignment &&
            assignment.Target == propertyReference)
        {
            return true;
        }

        // (o.Customer, o.Status) = (...): the property sits in a tuple on the target side.
        IOperation child = propertyReference;
        var parent = propertyReference.Parent;
        while (parent is ITupleOperation or IConversionOperation or IParenthesizedOperation)
        {
            child = parent;
            parent = parent.Parent;
        }

        return parent is IDeconstructionAssignmentOperation deconstruction &&
               deconstruction.Target == child;
    }

    private static bool IsCollectionMutatorReceiver(IPropertyReferenceOperation propertyReference)
    {
        if (propertyReference.Parent is IInvocationOperation parentCall &&
            parentCall.Instance == propertyReference &&
            CollectionMutatorMethods.Contains(parentCall.TargetMethod.Name))
        {
            return true;
        }

        // o?.Items?.Add(x): the mutator call hangs off a conditional access guarding the
        // navigation, so its instance is the placeholder rather than the property reference.
        return propertyReference.Parent is IConditionalAccessOperation conditionalAccess &&
               conditionalAccess.Operation == propertyReference &&
               conditionalAccess.WhenNotNull.UnwrapConversions() is IInvocationOperation conditionalCall &&
               conditionalCall.Instance is IConditionalAccessInstanceOperation &&
               CollectionMutatorMethods.Contains(conditionalCall.TargetMethod.Name);
    }

    private static bool LambdaReferencesTrackedLocal(
        IAnonymousFunctionOperation lambda,
        ILocalSymbol resultLocal,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken)
    {
        foreach (var descendant in lambda.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is ILocalReferenceOperation localReference &&
                (SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal) ||
                 entityLocals.Contains(localReference.Local)))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation? WalkUpThroughWrappers(IOperation? operation)
    {
        while (operation is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
            operation = operation.Parent;

        return operation;
    }

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

    private static ILocalSymbol? FindVariableAssignment(IInvocationOperation invocation)
    {
        var parent = invocation.Parent;

        while (parent != null)
        {
            if (parent is IVariableDeclaratorOperation declarator)
                return declarator.Symbol;

            if (parent is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localReference)
            {
                return localReference.Local;
            }

            if (parent is IExpressionStatementOperation or IReturnOperation)
                break;

            parent = parent.Parent;
        }

        return null;
    }
}
