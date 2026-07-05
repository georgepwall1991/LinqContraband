using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    /// <summary>
    /// True when a property of the materialized result — the result local, a foreach
    /// iteration variable over it, or an inline access on the materializer — is written
    /// (simple/compound assignment or increment/decrement). A mutation implies the entity
    /// is on a write path even if the SaveChanges lives in another method.
    /// </summary>
    private static bool MaterializedEntityIsMutated(IInvocationOperation materializer, CancellationToken cancellationToken)
    {
        // db.Users.First(...).Name = value — the mutation hangs directly off the materializer.
        var upwardParent = WalkUpThroughWrappers(materializer.Parent);
        if (upwardParent is IPropertyReferenceOperation inlineProperty && IsPropertyWriteTarget(inlineProperty))
            return true;

        var root = materializer.FindOwningExecutableRoot();
        if (root == null)
            return false;

        var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        // A repointed local could have been mutated while it held some other object, so
        // only a single-assignment result local counts as entity-bearing.
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
        if (upwardParent is IForEachLoopOperation inlineForEach &&
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

        if (entityLocals.Count == 0)
            return false;

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = descendant switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation incrementOrDecrement => incrementOrDecrement.Target,
                _ => null
            };

            // Indexer write targets are excluded: users[0] = new User() replaces a
            // collection element, it does not modify a materialized entity's state.
            if (target?.UnwrapConversions() is IPropertyReferenceOperation propertyReference &&
                IsMaterializedEntityPropertyWrite(propertyReference, entityLocals))
            {
                return true;
            }
        }

        return false;
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
            _ => false
        };
    }

    private static bool IsPropertyWriteTarget(IPropertyReferenceOperation propertyReference)
    {
        return (propertyReference.Parent is IAssignmentOperation assignment && assignment.Target == propertyReference) ||
               (propertyReference.Parent is IIncrementOrDecrementOperation incrementOrDecrement &&
                incrementOrDecrement.Target == propertyReference);
    }

    /// <summary>
    /// The local the materializer's VALUE is stored into. Only wrapper nodes may sit
    /// between the materializer and the declarator/assignment — anything else (an object
    /// initializer, an argument position, a member access) means the local holds some
    /// derived object, not the materialized entity.
    /// </summary>
    private static ILocalSymbol? FindResultLocal(IInvocationOperation materializer)
    {
        IOperation current = materializer;
        var parent = materializer.Parent;

        while (parent != null)
        {
            switch (parent)
            {
                case IConversionOperation or IParenthesizedOperation or IAwaitOperation
                    or IVariableInitializerOperation:
                    current = parent;
                    parent = parent.Parent;
                    continue;

                case IVariableDeclaratorOperation declarator:
                    return declarator.Symbol;

                case ISimpleAssignmentOperation assignment when
                    assignment.Value == current &&
                    assignment.Target is ILocalReferenceOperation localReference:
                    return localReference.Local;

                default:
                    return null;
            }
        }

        return null;
    }

    private static IOperation? WalkUpThroughWrappers(IOperation? operation)
    {
        while (operation is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
            operation = operation.Parent;

        return operation;
    }
}
