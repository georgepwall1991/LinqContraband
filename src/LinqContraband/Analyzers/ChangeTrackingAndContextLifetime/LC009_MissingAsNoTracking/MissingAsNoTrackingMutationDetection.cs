using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    /// <summary>
    /// The locals that hold the materialized result: the single-assignment result local and
    /// foreach iteration variables over it or over the inline materializer. A repointed local
    /// could have been mutated while it held some other object, so only a single-assignment
    /// result local counts as entity-bearing.
    /// </summary>
    private static HashSet<ILocalSymbol> CollectEntityLocals(
        IInvocationOperation materializer,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        var resultLocal = FindResultLocal(materializer);
        if (resultLocal != null &&
            LocalAssignmentCache.GetAssignments(root, resultLocal, cancellationToken).Count == 1)
        {
            entityLocals.Add(resultLocal);
        }
        else
        {
            resultLocal = null;
        }

        // foreach (var u in db.Users.ToList()) — iteration variables over the inline
        // materializer or over the result local are entity-bearing.
        if (WalkUpThroughWrappers(materializer.Parent) is IForEachLoopOperation inlineForEach &&
            inlineForEach.Collection.UnwrapConversions() == materializer)
        {
            foreach (var loopLocal in inlineForEach.Locals)
                entityLocals.Add(loopLocal);
        }

        if (resultLocal != null)
        {
            foreach (var descendant in root.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (descendant is IForEachLoopOperation forEach &&
                    forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                    SymbolEqualityComparer.Default.Equals(collectionRef.Local, resultLocal))
                {
                    foreach (var loopLocal in forEach.Locals)
                        entityLocals.Add(loopLocal);
                }
            }
        }

        return entityLocals;
    }

    /// <summary>
    /// True when the materialized result is changed in the same body: a property or field of
    /// the entity is written (assignment, compound assignment, increment/decrement), a method of
    /// the entity's own type is called (<c>order.Ship()</c>), a navigation collection is changed
    /// (<c>order.Lines.Add(line)</c>), or the entity is the destination of a mapper
    /// (<c>mapper.Map(dto, order)</c>, <c>patch.ApplyTo(order)</c>). Any of these puts the entity
    /// on a write path even if the SaveChanges lives in another method.
    /// </summary>
    private static bool MaterializedEntityIsMutated(
        IInvocationOperation materializer,
        IOperation root,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken)
    {
        // db.Users.First(...).Name = value — the mutation hangs directly off the materializer.
        var upwardParent = WalkUpThroughWrappers(materializer.Parent);
        if (upwardParent is IMemberReferenceOperation inlineMember &&
            inlineMember is IPropertyReferenceOperation or IFieldReferenceOperation &&
            IsMemberWriteTarget(inlineMember))
            return true;

        // db.Orders.First(...).Ship()
        if (upwardParent is IInvocationOperation inlineCall &&
            inlineCall.Instance?.UnwrapConversions() == materializer &&
            IsEntityStateMethod(inlineCall.TargetMethod))
            return true;

        if (entityLocals.Count == 0)
            return false;

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
                case IAssignmentOperation assignment when IsEntityMemberWrite(assignment.Target, entityLocals):
                    return true;
                case IIncrementOrDecrementOperation incrementOrDecrement when IsEntityMemberWrite(incrementOrDecrement.Target, entityLocals):
                    return true;
                case IInvocationOperation invocation when IsEntityMutatingCall(invocation, entityLocals):
                    return true;
            }
        }

        return false;
    }

    // Indexer write targets are excluded: users[0] = new User() replaces a collection element,
    // it does not modify a materialized entity's state.
    private static bool IsEntityMemberWrite(IOperation target, HashSet<ILocalSymbol> entityLocals)
    {
        return target.UnwrapConversions() switch
        {
            IPropertyReferenceOperation propertyReference => IsMaterializedEntityPropertyWrite(propertyReference, entityLocals),
            IFieldReferenceOperation fieldReference => IsRootedInMaterializedEntityLocal(fieldReference.Instance, entityLocals),
            _ => false
        };
    }

    private static bool IsEntityMutatingCall(IInvocationOperation invocation, HashSet<ILocalSymbol> entityLocals)
    {
        var instance = invocation.Instance?.UnwrapConversions();
        if (instance is ILocalReferenceOperation localReference)
        {
            // user.Deactivate() on the entity itself. Methods of the result list
            // (users.Add(...), users.Sort()) change the local list, not an entity.
            if (entityLocals.Contains(localReference.Local) && IsEntityStateMethod(invocation.TargetMethod))
                return true;
        }
        else if (instance is IPropertyReferenceOperation or IFieldReferenceOperation &&
                 IsRootedInMaterializedEntityLocal(instance, entityLocals))
        {
            // order.Lines.Add(line) or order.Address.Change(...)
            if (IsEntityStateMethod(invocation.TargetMethod) || IsCollectionMutator(invocation.TargetMethod))
                return true;
        }

        return IsMapperDestination(invocation, entityLocals);
    }

    /// <summary>
    /// An instance method declared by application code (the entity's own type or an owned
    /// value), which may change entity state. Framework types and the object members
    /// (<c>ToString</c>, <c>Equals</c>, ...) are reads.
    /// </summary>
    private static bool IsEntityStateMethod(IMethodSymbol method)
    {
        if (method.IsStatic || IsSystemNamespace(method.ContainingType?.ContainingNamespace))
            return false;

        return method.Name is not ("ToString" or "Equals" or "GetHashCode" or "GetType" or "CompareTo");
    }

    private static bool IsCollectionMutator(IMethodSymbol method)
    {
        return method.Name is "Add" or "AddRange" or "Insert" or "InsertRange" or "Remove" or "RemoveAll"
            or "RemoveAt" or "RemoveRange" or "RemoveWhere" or "Clear" or "TryAdd" or "Push" or "Enqueue"
            or "UnionWith" or "ExceptWith" or "IntersectWith" or "SymmetricExceptWith";
    }

    // mapper.Map(dto, entity) writes into its destination; patch.ApplyTo(entity) writes into its target.
    private static bool IsMapperDestination(IInvocationOperation invocation, HashSet<ILocalSymbol> entityLocals)
    {
        var name = invocation.TargetMethod.Name;
        if (name != "Map" && name != "ApplyTo")
            return false;

        var firstDestinationIndex = name == "Map" ? 1 : 0;
        if (invocation.TargetMethod.IsExtensionMethod)
            firstDestinationIndex++;

        for (var index = firstDestinationIndex; index < invocation.Arguments.Length; index++)
        {
            if (invocation.Arguments[index].Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
                entityLocals.Contains(argumentLocal.Local))
                return true;
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var name = namespaceSymbol?.ToDisplayString();
        return name == "System" || name?.StartsWith("System.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsMaterializedEntityPropertyWrite(
        IPropertyReferenceOperation propertyReference,
        HashSet<ILocalSymbol> entityLocals)
    {
        return !propertyReference.Property.IsIndexer &&
               IsRootedInMaterializedEntityLocal(propertyReference.Instance, entityLocals);
    }

    private static bool IsRootedInMaterializedEntityLocal(
        IOperation? operation,
        HashSet<ILocalSymbol> entityLocals)
    {
        var unwrapped = operation?.UnwrapConversions();

        return unwrapped switch
        {
            ILocalReferenceOperation localReference => entityLocals.Contains(localReference.Local),
            IPropertyReferenceOperation propertyReference when !propertyReference.Property.IsIndexer =>
                IsRootedInMaterializedEntityLocal(propertyReference.Instance, entityLocals),
            IFieldReferenceOperation fieldReference =>
                IsRootedInMaterializedEntityLocal(fieldReference.Instance, entityLocals),
            _ => false
        };
    }

    private static bool IsMemberWriteTarget(IOperation memberReference)
    {
        return (memberReference.Parent is IAssignmentOperation assignment && assignment.Target == memberReference) ||
               (memberReference.Parent is IIncrementOrDecrementOperation incrementOrDecrement &&
                incrementOrDecrement.Target == memberReference);
    }

}
