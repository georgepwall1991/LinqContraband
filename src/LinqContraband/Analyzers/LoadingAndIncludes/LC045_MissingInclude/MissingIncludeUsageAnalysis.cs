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
                : CollectInlineAccesses(materializer, entityType, entityTypes);
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

                    case IVariableDeclaratorOperation declarator when
                        declarator.Initializer != null &&
                        IsIndexedAccessOf(declarator.Initializer.Value, resultLocal):
                        entityLocals.Add(declarator.Symbol);
                        break;

                    case ISimpleAssignmentOperation assignmentOperation when
                        assignmentOperation.Target is ILocalReferenceOperation targetLocal &&
                        IsIndexedAccessOf(assignmentOperation.Value, resultLocal):
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

        var accesses = new List<NavigationAccess>();

        foreach (var descendant in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
                case IReturnOperation returnOperation when
                    returnOperation.ReturnedValue != null &&
                    IsTrackedLocal(returnOperation.ReturnedValue):
                    return null;

                case IInvocationOperation call when call != materializer:
                    if (IsTrackedLocal(call.Instance))
                        return null;
                    foreach (var argument in call.Arguments)
                    {
                        if (IsTrackedLocal(argument.Value))
                            return null;
                    }

                    break;

                case IAnonymousFunctionOperation lambda when
                    LambdaReferencesTrackedLocal(lambda, resultLocal, entityLocals, cancellationToken):
                    return null;

                case ISimpleAssignmentOperation assignmentOperation when
                    IsTrackedLocal(assignmentOperation.Value) &&
                    assignmentOperation.Target.UnwrapConversions() is not ILocalReferenceOperation:
                    return null;

                case IPropertyReferenceOperation propertyReference:
                    if (IsWriteUsage(propertyReference))
                        break;
                    if (TryGetAccessPath(propertyReference, entityType, entityTypes, IsTrackedLocal, out var path))
                        accesses.Add(new NavigationAccess(path, propertyReference.Syntax));
                    break;
            }
        }

        return accesses;
    }

    private static List<NavigationAccess> CollectInlineAccesses(
        IInvocationOperation materializer,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes)
    {
        var accesses = new List<NavigationAccess>();
        var current = WalkUpThroughWrappers(materializer.Parent);
        var currentEntity = entityType;
        string? path = null;

        while (current is IPropertyReferenceOperation propertyReference &&
               IsPropertyOfEntity(propertyReference.Property, currentEntity) &&
               TryGetNavigationTarget(propertyReference.Property, entityTypes, out var target, out var isCollection))
        {
            if (IsWriteUsage(propertyReference))
                break;

            path = path == null ? propertyReference.Property.Name : path + "." + propertyReference.Property.Name;
            accesses.Add(new NavigationAccess(path, propertyReference.Syntax));

            if (isCollection)
                break;

            currentEntity = target;
            current = WalkUpThroughWrappers(propertyReference.Parent);
        }

        return accesses;
    }

    private static bool TryGetAccessPath(
        IPropertyReferenceOperation propertyReference,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        System.Func<IOperation?, bool> isTrackedLocal,
        out string path)
    {
        path = null!;

        if (!TryGetNavigationTarget(propertyReference.Property, entityTypes, out _, out _))
            return false;

        var instance = propertyReference.Instance?.UnwrapConversions();

        if (instance is ILocalReferenceOperation && isTrackedLocal(instance))
        {
            if (!IsPropertyOfEntity(propertyReference.Property, entityType))
                return false;

            path = propertyReference.Property.Name;
            return true;
        }

        // Nested access through a reference navigation: o.Customer.Address => "Customer.Address".
        if (instance is IPropertyReferenceOperation parentReference &&
            TryGetAccessPath(parentReference, entityType, entityTypes, isTrackedLocal, out var parentPath))
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

        return false;
    }

    private static bool IsWriteUsage(IPropertyReferenceOperation propertyReference)
    {
        // o.Customer = c: relationship fix-up, not a read of unloaded data.
        if (propertyReference.Parent is IAssignmentOperation assignment &&
            assignment.Target == propertyReference)
        {
            return true;
        }

        // o.Items.Add(x): mutating a tracked entity's collection does not require loading it.
        return propertyReference.Parent is IInvocationOperation parentCall &&
               parentCall.Instance == propertyReference &&
               CollectionMutatorMethods.Contains(parentCall.TargetMethod.Name);
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
        if (unwrapped is IPropertyReferenceOperation propertyReference && propertyReference.Arguments.Length > 0)
        {
            var instance = propertyReference.Instance?.UnwrapConversions();
            if (instance is ILocalReferenceOperation localReference &&
                SymbolEqualityComparer.Default.Equals(localReference.Local, collectionLocal))
            {
                return true;
            }
        }

        if (unwrapped is IArrayElementReferenceOperation arrayElement &&
            arrayElement.ArrayReference.UnwrapConversions() is ILocalReferenceOperation arrayLocal &&
            SymbolEqualityComparer.Default.Equals(arrayLocal.Local, collectionLocal))
        {
            return true;
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
