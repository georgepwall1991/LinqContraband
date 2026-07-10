using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static readonly ImmutableHashSet<string> CollectionMutatorMethods =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "Add",
            "AddRange",
            "Remove",
            "RemoveRange",
            "Clear"
        );

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
        if (
            propertyReference.Parent is IAssignmentOperation assignment
            && assignment.Target == propertyReference
        )
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

        return parent is IDeconstructionAssignmentOperation deconstruction
            && deconstruction.Target == child;
    }

    private static bool IsCollectionMutatorReceiver(IPropertyReferenceOperation propertyReference)
    {
        if (
            propertyReference.Parent is IInvocationOperation parentCall
            && parentCall.Instance == propertyReference
            && CollectionMutatorMethods.Contains(parentCall.TargetMethod.Name)
        )
        {
            return true;
        }

        // o?.Items?.Add(x): the mutator call hangs off a conditional access guarding the
        // navigation, so its instance is the placeholder rather than the property reference.
        return propertyReference.Parent is IConditionalAccessOperation conditionalAccess
            && conditionalAccess.Operation == propertyReference
            && conditionalAccess.WhenNotNull.UnwrapConversions()
                is IInvocationOperation conditionalCall
            && conditionalCall.Instance is IConditionalAccessInstanceOperation
            && CollectionMutatorMethods.Contains(conditionalCall.TargetMethod.Name);
    }

    private static bool LambdaReferencesTrackedLocal(
        IAnonymousFunctionOperation lambda,
        ILocalSymbol resultLocal,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken
    )
    {
        foreach (var descendant in lambda.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (
                descendant is ILocalReferenceOperation localReference
                && (
                    SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal)
                    || entityLocals.Contains(localReference.Local)
                )
            )
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

    private static ILocalSymbol? FindVariableAssignment(IInvocationOperation invocation)
    {
        IOperation value = invocation;
        while (true)
        {
            if (value.Parent is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
            {
                value = value.Parent;
                continue;
            }

            if (
                value.Parent is IInvocationOperation configureAwait
                && configureAwait.Instance?.UnwrapConversions() == value
                && IsFrameworkConfigureAwait(configureAwait)
            )
            {
                value = configureAwait;
                continue;
            }

            break;
        }

        if (
            value.Parent is IVariableInitializerOperation initializer
            && initializer.Value == value
            && initializer.Parent is IVariableDeclaratorOperation declarator
        )
        {
            return declarator.Symbol;
        }

        if (
            value.Parent is ISimpleAssignmentOperation assignment
            && assignment.Value == value
            && assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference
        )
        {
            return localReference.Local;
        }

        return null;
    }

    private static bool IsFrameworkConfigureAwait(IInvocationOperation invocation)
    {
        if (
            invocation.TargetMethod.Name != "ConfigureAwait"
            || invocation.Arguments.Length != 1
            || invocation.Instance == null
        )
        {
            return false;
        }

        var containingType = invocation.TargetMethod.ContainingType;
        return containingType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && containingType.Name is "Task" or "ValueTask";
    }
}
