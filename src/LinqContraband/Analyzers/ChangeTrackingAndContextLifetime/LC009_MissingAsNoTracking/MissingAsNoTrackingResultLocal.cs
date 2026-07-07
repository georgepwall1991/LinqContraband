using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
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
