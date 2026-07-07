using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
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
        // to the outer one: its owner is the nearest ancestor reached from the WhenNotNull
        // side. Matching the first IConditionalAccessOperation ancestor regardless of side
        // returns the inner access, whose Operation is the same property reference being
        // resolved, and TryGetAccessPath then recurses on its own input until the stack
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
}
