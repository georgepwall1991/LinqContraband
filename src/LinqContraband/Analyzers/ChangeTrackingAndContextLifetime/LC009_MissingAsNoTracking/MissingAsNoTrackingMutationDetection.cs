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

        var resultLocal = FindResultLocal(materializer);
        if (resultLocal != null)
            entityLocals.Add(resultLocal);

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

            if (target?.UnwrapConversions() is IPropertyReferenceOperation propertyReference &&
                propertyReference.Instance?.UnwrapConversions() is ILocalReferenceOperation instanceLocal &&
                entityLocals.Contains(instanceLocal.Local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPropertyWriteTarget(IPropertyReferenceOperation propertyReference)
    {
        return (propertyReference.Parent is IAssignmentOperation assignment && assignment.Target == propertyReference) ||
               (propertyReference.Parent is IIncrementOrDecrementOperation incrementOrDecrement &&
                incrementOrDecrement.Target == propertyReference);
    }

    private static ILocalSymbol? FindResultLocal(IInvocationOperation materializer)
    {
        var parent = materializer.Parent;

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

    private static IOperation? WalkUpThroughWrappers(IOperation? operation)
    {
        while (operation is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
            operation = operation.Parent;

        return operation;
    }
}
