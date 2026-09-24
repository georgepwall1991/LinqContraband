using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
    private static bool IsTrackedEntityReference(
        IOperation? operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        if (operation == null) return false;

        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is ILocalReferenceOperation localReference)
        {
            return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                   foreachLocals.Contains(localReference.Local) ||
                   manualIterationLocals.Contains(localReference.Local);
        }

        return false;
    }

    private static bool IsRootedInTrackedEntity(
        IOperation? operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        var current = operation?.UnwrapConversions();
        while (current is IMemberReferenceOperation { Instance: { } instance })
            current = instance.UnwrapConversions();

        return IsTrackedEntityReference(current, variable, foreachLocals, manualIterationLocals);
    }

    private static readonly HashSet<string> CollectionMutators = new()
    {
        "Add", "AddRange", "Remove", "RemoveAll", "RemoveAt", "RemoveRange", "RemoveWhere", "Clear", "Insert", "InsertRange"
    };

    private static bool IsEntityMutatingCall(
        IInvocationOperation call,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        var instance = call.Instance?.UnwrapConversions();
        if (instance == null)
            return false;

        // A method declared on the entity itself, such as a domain method, can change any of its state.
        if (IsTrackedEntityReference(instance, variable, foreachLocals, manualIterationLocals))
            return !IsSequence(instance.Type) &&
                   call.TargetMethod.ContainingType?.SpecialType != SpecialType.System_Object &&
                   call.TargetMethod.Name is not ("ToString" or "GetHashCode" or "Equals" or "GetType");

        // A mutating call on a collection reached through the entity: `lib.Orders.Add(order)`.
        return instance is IMemberReferenceOperation &&
               CollectionMutators.Contains(call.TargetMethod.Name) &&
               IsRootedInTrackedEntity(instance, variable, foreachLocals, manualIterationLocals);
    }

    // The materialized list itself (`users.Contains(x)`) is not an entity; its elements are.
    private static bool IsSequence(ITypeSymbol? type)
    {
        if (type == null || type.SpecialType == SpecialType.System_String)
            return false;

        foreach (var implemented in type.AllInterfaces)
        {
            if (implemented.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
        }

        return false;
    }

    private static bool IsDirectVariableEscape(
        IOperation operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is not ILocalReferenceOperation localReference) return false;

        return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
               foreachLocals.Contains(localReference.Local) ||
               manualIterationLocals.Contains(localReference.Local);
    }

    private static bool LambdaDirectlyReferences(
        IAnonymousFunctionOperation lambda,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        CancellationToken cancellationToken)
    {
        foreach (var descendant in lambda.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is not ILocalReferenceOperation localReference) continue;

            if (SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                foreachLocals.Contains(localReference.Local) ||
                manualIterationLocals.Contains(localReference.Local))
            {
                return true;
            }
        }

        return false;
    }
}
