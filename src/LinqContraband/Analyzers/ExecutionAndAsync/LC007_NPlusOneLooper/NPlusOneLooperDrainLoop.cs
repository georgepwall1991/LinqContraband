using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A batch drain loop: a <c>while</c> or <c>do</c> loop in which one query per iteration is bounded by
/// <c>Take(n)</c> and its result ends the loop (the row count an <c>ExecuteDelete</c> returns, or the length, count or
/// emptiness of the materialized batch). The loop runs about rows / n times, so every query in it, including the
/// delete or update that works on the batch, runs once per batch rather than once per item.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private static bool IsTakeBoundedDrainLoop(ILoopOperation loop, IOperation? condition, CancellationToken cancellationToken)
    {
        foreach (var operation in DescendantsInSameBody(loop.Body))
        {
            if (operation is not IInvocationOperation gate ||
                !IsTakeBounded(gate) ||
                gate.FindEnclosingLoop() != loop ||
                !TryMatchDatabaseExecution(gate, cancellationToken, out _))
            {
                continue;
            }

            var batchLocals = GetResultDerivedLocals(gate, loop);
            if (IsConditionFreeOfItemSources(condition, batchLocals, cancellationToken) &&
                ResultControlsLoopExit(loop, condition, batchLocals))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The query's own chain calls <c>Queryable.Take</c> with a count that is not a constant 0 or 1.</summary>
    private static bool IsTakeBounded(IInvocationOperation invocation)
    {
        for (var current = invocation.GetInvocationReceiver()?.UnwrapConversions();
             current is IInvocationOperation call;
             current = call.GetInvocationReceiver()?.UnwrapConversions())
        {
            var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
            if (!IsMethod(method, "System.Linq", "Queryable", "Take") || call.Arguments.Length != 2)
                continue;

            var count = call.Arguments[1].Value.UnwrapConversions();
            if (count.Type?.SpecialType != SpecialType.System_Int32)
                continue;

            return count.ConstantValue is not { HasValue: true, Value: int rows } || rows > 1;
        }

        return false;
    }

    /// <summary>
    /// A number read from settings rather than from an item source: an integer field or property reached through
    /// <c>this</c>, fields, properties, parameters or locals, none of which is a collection
    /// (<c>_options.BatchSize</c>, <c>BatchSize</c>). <c>queue.Count</c> and <c>ids.Length</c> do not qualify.
    /// </summary>
    private static bool IsConfiguredCount(IMemberReferenceOperation member)
    {
        if (!IsIntegral(member.Type))
            return false;

        for (var instance = member.Instance; instance != null;)
        {
            if (IsCollection(instance.Type))
                return false;

            switch (instance)
            {
                case IInstanceReferenceOperation:
                case IParameterReferenceOperation:
                case ILocalReferenceOperation:
                    return true;
                case IFieldReferenceOperation field:
                    instance = field.Instance;
                    break;
                case IPropertyReferenceOperation { Arguments.Length: 0 } property:
                    instance = property.Instance;
                    break;
                default:
                    return false;
            }
        }

        // A static member.
        return true;
    }

    private static bool IsIntegral(ITypeSymbol? type)
    {
        switch (type?.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return true;
            default:
                return false;
        }
    }

    private static bool IsCollection(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol)
            return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (implemented.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
        }

        return type.SpecialType == SpecialType.System_Collections_IEnumerable;
    }
}
