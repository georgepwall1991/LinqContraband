using LinqContraband.Extensions;
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

    /// <summary>
    /// Where the entities of this EF materializer end up once in-memory LINQ is done with them.
    /// In <c>var orders = db.Orders.ToList().Where(...).ToList();</c> the EF query runs at the inner
    /// <c>ToList()</c>, but the entities are held, changed or handed on through the outer one, so
    /// that outer LINQ-to-Objects materializer is where result locals, mutations and escapes are
    /// looked for. It is the last query-executing materializer reached through LINQ-to-Objects
    /// operators that still carry the entity; without one, the EF materializer itself.
    /// </summary>
    private static IInvocationOperation FindInMemoryResultAnchor(IInvocationOperation materializer)
    {
        var entityType = materializer.TargetMethod.TypeArguments.Length > 0
            ? materializer.TargetMethod.TypeArguments[0]
            : null;
        if (entityType == null)
            return materializer;

        var anchor = materializer;
        IOperation current = materializer;

        while (WalkUpThroughWrappers(current.Parent) is IArgumentOperation { Parent: IInvocationOperation linq } argument &&
               IsLinqToObjectsSource(linq, argument) &&
               ContainsType(linq.Type, entityType))
        {
            current = linq;
            if (linq.IsQueryExecutingMaterializer())
                anchor = linq;
        }

        return anchor;
    }

    private static IOperation? WalkUpThroughWrappers(IOperation? operation)
    {
        while (operation is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
            operation = operation.Parent;

        return operation;
    }
}
