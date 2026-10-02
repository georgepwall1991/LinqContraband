using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A load-once guard runs its query at most once however many times the loop goes round:
/// <c>if (x == null) x = query;</c>, <c>if (x is null) { x = query; }</c> or <c>x ??= query;</c>, where <c>x</c> is a
/// local declared before the loop and nothing else writes it inside the loop or from a lambda or local function.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private static bool IsLoadOnceGuardedInLoop(IInvocationOperation invocation, ILoopOperation loop)
    {
        var statement = FindEnclosingExpressionStatement(invocation);
        if (statement == null || !loop.Syntax.Span.Contains(statement.Syntax.Span))
            return false;

        IAssignmentOperation assignment;
        switch (statement.Operation)
        {
            case ICoalesceAssignmentOperation coalesceAssignment:
                assignment = coalesceAssignment;
                break;
            case ISimpleAssignmentOperation simpleAssignment when IsGuardedByNullCheckOf(statement, simpleAssignment.Target):
                assignment = simpleAssignment;
                break;
            default:
                return false;
        }

        if (assignment.Target is not ILocalReferenceOperation targetReference ||
            !assignment.Value.Syntax.Span.Contains(invocation.Syntax.Span))
        {
            return false;
        }

        var local = targetReference.Local;
        if (local.IsRef ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            loop.Syntax.Span.Contains(local.DeclaringSyntaxReferences[0].Span))
        {
            return false;
        }

        return !HasOtherWriteThatCanRearm(local, assignment, loop);
    }

    private static IExpressionStatementOperation? FindEnclosingExpressionStatement(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case IExpressionStatementOperation statement:
                    return statement;
                case IAnonymousFunctionOperation or ILocalFunctionOperation or IMethodBodyBaseOperation:
                    return null;
            }
        }

        return null;
    }

    private static bool IsGuardedByNullCheckOf(IExpressionStatementOperation statement, IOperation target)
    {
        if (target is not ILocalReferenceOperation targetReference)
            return false;

        IOperation guarded = statement;
        if (statement.Parent is IBlockOperation block && block.Parent is IConditionalOperation)
            guarded = block;

        return guarded.Parent is IConditionalOperation conditional &&
               ReferenceEquals(conditional.WhenTrue, guarded) &&
               ConditionImpliesNull(conditional.Condition, targetReference.Local);
    }

    private static bool ConditionImpliesNull(IOperation condition, ILocalSymbol local)
    {
        condition = condition.UnwrapConversions();
        switch (condition)
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction:
                return ConditionImpliesNull(conjunction.LeftOperand, local) ||
                       ConditionImpliesNull(conjunction.RightOperand, local);
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals:
                return (IsLocalRead(equals.LeftOperand, local) && IsNullConstant(equals.RightOperand)) ||
                       (IsLocalRead(equals.RightOperand, local) && IsNullConstant(equals.LeftOperand));
            case IIsPatternOperation { Pattern: IConstantPatternOperation constantPattern } isPattern:
                return IsLocalRead(isPattern.Value, local) && IsNullConstant(constantPattern.Value);
            default:
                return false;
        }
    }

    private static bool IsLocalRead(IOperation operation, ILocalSymbol local)
    {
        return operation.UnwrapConversions() is ILocalReferenceOperation reference &&
               SymbolEqualityComparer.Default.Equals(reference.Local, local);
    }

    private static bool IsNullConstant(IOperation operation)
    {
        var constant = operation.UnwrapConversions().ConstantValue;
        return constant.HasValue && constant.Value == null;
    }

    /// <summary>
    /// Any other write to the guarded local inside the loop, or from a lambda or local function anywhere in the
    /// method, could reset it between iterations. Writes before the loop in the same body are fine.
    /// </summary>
    private static bool HasOtherWriteThatCanRearm(ILocalSymbol local, IAssignmentOperation guardedAssignment, ILoopOperation loop)
    {
        var root = loop.FindOwningExecutableRoot();
        if (root == null)
            return true;

        foreach (var reference in root.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local) ||
                ReferenceEquals(reference, guardedAssignment.Target) ||
                !IsWriteReference(reference))
            {
                continue;
            }

            if (loop.Syntax.Span.Contains(reference.Syntax.Span) ||
                !ReferenceEquals(reference.FindOwningExecutableRoot(), root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWriteReference(ILocalReferenceOperation reference)
    {
        if (reference.IsDeclaration)
            return false;

        var parent = reference.Parent;
        return parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, reference),
            IIncrementOrDecrementOperation => true,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            ITupleOperation or IDeclarationExpressionOperation => true,
            _ => false
        };
    }
}
