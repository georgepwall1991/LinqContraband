using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A save every N items keeps the change tracker small on a long job without paying a round trip per item:
/// <c>if (++pending % 1000 == 0) db.SaveChanges();</c>, <c>if (++pending &gt;= size) { db.SaveChanges(); pending = 0; }</c>
/// or <c>if (buffer.Count &gt;= size) { ...; db.SaveChanges(); buffer.Clear(); }</c>. The save is exempt for a loop
/// when the counter or buffer lives across that loop's passes and the guard cannot hold on every pass.
/// </summary>
public sealed partial class SaveChangesInLoopAnalyzer
{
    /// <summary>
    /// The save sits in the true branch of an <c>if</c> that holds once every N passes of <paramref name="loop"/>.
    /// </summary>
    private static bool IsPeriodicFlush(IOperation save, ILoopOperation loop)
    {
        IOperation child = save;
        for (var current = save.Parent; current != null && current != loop; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;

            if (current is IConditionalOperation { WhenTrue: { } whenTrue } conditional &&
                child == whenTrue &&
                HoldsEveryNthPass(conditional.Condition, conditional, loop))
            {
                return true;
            }

            child = current;
        }

        return false;
    }

    private static bool HoldsEveryNthPass(IOperation condition, IConditionalOperation conditional, ILoopOperation loop)
    {
        if (condition.UnwrapConversions() is not IBinaryOperation comparison)
            return false;

        // A conjunction only narrows the guard: `i > 0 && i % 500 == 0`.
        if (comparison.OperatorKind == BinaryOperatorKind.ConditionalAnd)
        {
            return HoldsEveryNthPass(comparison.LeftOperand, conditional, loop) ||
                   HoldsEveryNthPass(comparison.RightOperand, conditional, loop);
        }

        var left = comparison.LeftOperand.UnwrapConversions();
        var right = comparison.RightOperand.UnwrapConversions();
        switch (comparison.OperatorKind)
        {
            case BinaryOperatorKind.Equals:
                return IsCounterRemainder(left, right, conditional, loop) ||
                       IsCounterRemainder(right, left, conditional, loop) ||
                       IsThresholdWithReset(left, right, 2, conditional, loop) ||
                       IsThresholdWithReset(right, left, 2, conditional, loop);
            case BinaryOperatorKind.GreaterThanOrEqual:
                return IsThresholdWithReset(left, right, 2, conditional, loop);
            case BinaryOperatorKind.GreaterThan:
                return IsThresholdWithReset(left, right, 1, conditional, loop);
            case BinaryOperatorKind.LessThanOrEqual:
                return IsThresholdWithReset(right, left, 2, conditional, loop);
            case BinaryOperatorKind.LessThan:
                return IsThresholdWithReset(right, left, 1, conditional, loop);
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>counter % n == k</c>, where the counter is an integer local the loop only ever advances by one.
    /// </summary>
    private static bool IsCounterRemainder(
        IOperation remainderSide,
        IOperation comparand,
        IConditionalOperation conditional,
        ILoopOperation loop)
    {
        if (!comparand.ConstantValue.HasValue ||
            remainderSide is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Remainder } remainder)
        {
            return false;
        }

        // `% 1` holds on every pass. A divisor that is not a constant is a configured batch size.
        if (TryGetIntegerConstant(remainder.RightOperand.UnwrapConversions(), out var divisor) && divisor <= 1)
            return false;

        return TryGetCounterLocal(remainder.LeftOperand, out var counter) &&
               LivesAcrossPasses(counter, loop, allowForLoopVariable: true) &&
               AdvancesByOne(counter, conditional, loop);
    }

    /// <summary>
    /// <c>pending &gt;= n</c> with <c>pending = 0;</c> in the branch, or <c>buffer.Count &gt;= n</c> with
    /// <c>buffer.Clear();</c>: after the reset, the guard needs n more items before it holds again.
    /// </summary>
    private static bool IsThresholdWithReset(
        IOperation counterSide,
        IOperation thresholdSide,
        long minimumThreshold,
        IConditionalOperation conditional,
        ILoopOperation loop)
    {
        if (TryGetIntegerConstant(thresholdSide, out var threshold) && threshold < minimumThreshold)
            return false;

        ILocalSymbol counter;
        bool isBuffer;
        switch (counterSide)
        {
            case IPropertyReferenceOperation { Property.Name: "Count", Instance: { } instance }
                when instance.UnwrapConversions() is ILocalReferenceOperation bufferReference:
                counter = bufferReference.Local;
                isBuffer = true;
                break;
            case ILocalReferenceOperation counterReference:
                counter = counterReference.Local;
                isBuffer = false;
                break;
            case IIncrementOrDecrementOperation
            {
                Kind: OperationKind.Increment,
                Target: ILocalReferenceOperation steppedReference
            }:
                counter = steppedReference.Local;
                isBuffer = false;
                break;
            default:
                return false;
        }

        if (!isBuffer && !IsIntegerType(counter.Type))
            return false;

        if (!LivesAcrossPasses(counter, loop, allowForLoopVariable: false))
            return false;

        foreach (var operation in conditional.WhenTrue!.DescendantsAndSelf())
        {
            if (IsResetOf(operation, counter, isBuffer) && !IsInsideNestedFunction(operation, conditional))
                return true;
        }

        return false;
    }

    private static bool IsResetOf(IOperation operation, ILocalSymbol counter, bool isBuffer)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                when SymbolEqualityComparer.Default.Equals(target.Local, counter):
                var value = assignment.Value.UnwrapConversions();
                return isBuffer
                    ? value is IObjectCreationOperation { Arguments.Length: 0 }
                    : IsIntegerConstant(value, 0);
            case IInvocationOperation { TargetMethod.Name: "Clear", Arguments.Length: 0, Instance: { } instance }
                when isBuffer:
                return instance.UnwrapConversions() is ILocalReferenceOperation cleared &&
                       SymbolEqualityComparer.Default.Equals(cleared.Local, counter);
            default:
                return false;
        }
    }

    /// <summary>
    /// The counter under the remainder: <c>pending</c>, <c>++pending</c>, <c>i + 1</c>.
    /// </summary>
    private static bool TryGetCounterLocal(IOperation operation, out ILocalSymbol counter)
    {
        counter = null!;
        for (var depth = 0; depth < 4; depth++)
        {
            switch (operation.UnwrapConversions())
            {
                case ILocalReferenceOperation reference:
                    counter = reference.Local;
                    return IsIntegerType(counter.Type);
                case IIncrementOrDecrementOperation
                {
                    Kind: OperationKind.Increment,
                    Target: ILocalReferenceOperation stepped
                }:
                    counter = stepped.Local;
                    return IsIntegerType(counter.Type);
                case IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract
                } offset when offset.RightOperand.ConstantValue.HasValue:
                    operation = offset.LeftOperand;
                    break;
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } offset
                    when offset.LeftOperand.ConstantValue.HasValue:
                    operation = offset.RightOperand;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The local is declared outside the loop's body and is not the loop's own item: a <c>foreach</c> variable or
    /// a variable the loop header assigns belongs to one pass. A <c>for</c> loop's counter is the exception.
    /// </summary>
    private static bool LivesAcrossPasses(ILocalSymbol local, ILoopOperation loop, bool allowForLoopVariable)
    {
        foreach (var loopLocal in loop.Locals)
        {
            if (SymbolEqualityComparer.Default.Equals(loopLocal, local))
                return allowForLoopVariable && loop is IForLoopOperation;
        }

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declaration = local.DeclaringSyntaxReferences[0];
        return declaration.SyntaxTree != loop.Body.Syntax.SyntaxTree ||
               !loop.Body.Syntax.Span.Contains(declaration.Span);
    }

    /// <summary>
    /// Every write to the counter inside <paramref name="loop"/> adds one in the loop that holds the guard
    /// (<c>pending++</c>, <c>pending += 1</c>, <c>pending = pending + 1</c>, or the <c>for</c> loop's own step),
    /// apart from a reset to zero in the guarded branch. A counter stepped by an outer loop, by another amount,
    /// or handed out by <c>ref</c> does not count passes of the loop that saves.
    /// </summary>
    private static bool AdvancesByOne(ILocalSymbol counter, IConditionalOperation conditional, ILoopOperation loop)
    {
        var steppingLoop = conditional.FindEnclosingLoop();
        if (steppingLoop == null)
            return false;

        var advances = false;
        foreach (var operation in loop.Descendants())
        {
            switch (operation)
            {
                case IIncrementOrDecrementOperation { Target: ILocalReferenceOperation target } step
                    when SymbolEqualityComparer.Default.Equals(target.Local, counter):
                    if (step.Kind != OperationKind.Increment || !RunsDirectlyIn(step, steppingLoop))
                        return false;

                    advances = true;
                    break;

                case ICompoundAssignmentOperation { Target: ILocalReferenceOperation target } compound
                    when SymbolEqualityComparer.Default.Equals(target.Local, counter):
                    if (compound.OperatorKind != BinaryOperatorKind.Add ||
                        !IsIntegerConstant(compound.Value.UnwrapConversions(), 1) ||
                        !RunsDirectlyIn(compound, steppingLoop))
                    {
                        return false;
                    }

                    advances = true;
                    break;

                case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                    when SymbolEqualityComparer.Default.Equals(target.Local, counter):
                    var value = assignment.Value.UnwrapConversions();
                    if (IsIntegerConstant(value, 0) &&
                        conditional.WhenTrue!.Syntax.Span.Contains(assignment.Syntax.Span))
                    {
                        break;
                    }

                    if (!IsCounterPlusOne(value, counter) || !RunsDirectlyIn(assignment, steppingLoop))
                        return false;

                    advances = true;
                    break;

                case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument
                    when argument.Value.UnwrapConversions() is ILocalReferenceOperation passed &&
                         SymbolEqualityComparer.Default.Equals(passed.Local, counter):
                    return false;
            }
        }

        return advances;
    }

    private static bool IsCounterPlusOne(IOperation value, ILocalSymbol counter)
    {
        if (value is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } sum)
            return false;

        var left = sum.LeftOperand.UnwrapConversions();
        var right = sum.RightOperand.UnwrapConversions();
        return IsReferenceTo(left, counter) && IsIntegerConstant(right, 1) ||
               IsReferenceTo(right, counter) && IsIntegerConstant(left, 1);
    }

    private static bool IsReferenceTo(IOperation operation, ILocalSymbol local)
    {
        return operation is ILocalReferenceOperation reference &&
               SymbolEqualityComparer.Default.Equals(reference.Local, local);
    }

    /// <summary>
    /// The operation's nearest loop is <paramref name="loop"/>, with no lambda or local function in between.
    /// </summary>
    private static bool RunsDirectlyIn(IOperation operation, ILoopOperation loop)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is ILoopOperation nearest)
                return nearest == loop;

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;
        }

        return false;
    }

    private static bool IsInsideNestedFunction(IOperation operation, IOperation root)
    {
        for (var current = operation.Parent; current != null && current != root; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return true;
        }

        return false;
    }

    private static bool IsIntegerConstant(IOperation operation, long expected)
    {
        return TryGetIntegerConstant(operation, out var value) && value == expected;
    }

    private static bool TryGetIntegerConstant(IOperation operation, out long value)
    {
        value = 0;
        if (!operation.ConstantValue.HasValue)
            return false;

        switch (operation.ConstantValue.Value)
        {
            case int number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case ushort number:
                value = number;
                return true;
            case byte number:
                value = number;
                return true;
            case sbyte number:
                value = number;
                return true;
            default:
                return false;
        }
    }

    private static bool IsIntegerType(ITypeSymbol? type)
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
}
