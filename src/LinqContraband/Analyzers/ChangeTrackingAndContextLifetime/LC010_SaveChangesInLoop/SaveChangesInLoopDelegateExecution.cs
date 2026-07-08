using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private const int MaxDelegateCallChainDepth = 4;

    private static bool IsInsideDelegateCalledFromLoop(IInvocationOperation invocation)
    {
        var anonymousFunction = FindDirectOwningAnonymousFunction(invocation);
        if (anonymousFunction != null)
        {
            if (FindContainingExecutableRootForDelegateTarget(anonymousFunction) is not { } containingRoot ||
                !TryGetDelegateLocalAssignment(
                    anonymousFunction,
                    out var local,
                    out var assignmentStart,
                    out var assignmentOperation))
            {
                return false;
            }

            return IsDelegateLocalOrAliasesCalledFromLoop(
                       containingRoot,
                       local,
                       assignmentStart,
                       assignmentOperation,
                       invocation) ||
                   containingRoot is ILocalFunctionOperation assignmentFunction &&
                   IsDelegateAssignedInsideCalledLocalFunctionCalledFromLoop(
                       assignmentFunction,
                       local,
                       assignmentStart,
                       assignmentOperation,
                       invocation);
        }

        var localFunction = FindDirectOwningLocalFunction(invocation);
        return localFunction != null &&
               FindContainingExecutableRoot(localFunction) is { } localRoot &&
               IsLocalFunctionDelegateCalledFromLoop(localRoot, localFunction.Symbol, invocation);
    }

    private static bool IsSaveMethodReferenceAssignedToDelegateCalledFromLoop(IMethodReferenceOperation methodReference)
    {
        if (FindContainingExecutableRootForDelegateTarget(methodReference) is not { } containingRoot ||
            !TryGetDelegateLocalAssignment(
                methodReference,
                out var local,
                out var assignmentStart,
                out var assignmentOperation))
        {
            return false;
        }

        return IsDelegateLocalOrAliasesCalledFromLoop(
                   containingRoot,
                   local,
                   assignmentStart,
                   assignmentOperation,
                   methodReference) ||
               containingRoot is ILocalFunctionOperation assignmentFunction &&
               IsDelegateAssignedInsideCalledLocalFunctionCalledFromLoop(
                   assignmentFunction,
                   local,
                   assignmentStart,
                   assignmentOperation,
                   methodReference);
    }

    private static IAnonymousFunctionOperation? FindDirectOwningAnonymousFunction(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
                return anonymousFunction;

            if (current is ILocalFunctionOperation or IMethodBodyBaseOperation)
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static IOperation? FindContainingExecutableRootForDelegateTarget(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }

    private static bool TryGetDelegateLocalAssignment(
        IOperation delegateTarget,
        out ILocalSymbol local,
        out int assignmentStart,
        out IOperation assignmentOperation)
    {
        var current = delegateTarget.Parent;
        while (current != null)
        {
            switch (current)
            {
                case IVariableDeclaratorOperation declarator
                    when IsAssignedDelegateTarget(declarator.Initializer?.Value, delegateTarget):
                    local = declarator.Symbol;
                    assignmentStart = declarator.Syntax.SpanStart;
                    assignmentOperation = declarator;
                    return true;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                         IsAssignedDelegateTarget(assignment.Value, delegateTarget):
                    local = localReference.Local;
                    assignmentStart = assignment.Syntax.SpanStart;
                    assignmentOperation = assignment;
                    return true;

                case ICompoundAssignmentOperation assignment
                    when assignment.OperatorKind == BinaryOperatorKind.Add &&
                         assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                         IsAssignedDelegateTarget(assignment.Value, delegateTarget):
                    local = localReference.Local;
                    assignmentStart = assignment.Syntax.SpanStart;
                    assignmentOperation = assignment;
                    return true;

                case IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation:
                    local = null!;
                    assignmentStart = -1;
                    assignmentOperation = null!;
                    return false;
            }

            current = current.Parent;
        }

        local = null!;
        assignmentStart = -1;
        assignmentOperation = null!;
        return false;
    }

    private static bool IsAssignedDelegateTarget(IOperation? value, IOperation delegateTarget)
    {
        if (value == null)
            return false;

        var assignedValue = value.UnwrapConversions();
        if (assignedValue is IDelegateCreationOperation delegateCreation)
            assignedValue = delegateCreation.Target.UnwrapConversions();

        if (assignedValue is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary)
        {
            return IsAssignedDelegateTarget(binary.LeftOperand, delegateTarget) ||
                   IsAssignedDelegateTarget(binary.RightOperand, delegateTarget);
        }

        if (ReferenceEquals(assignedValue, delegateTarget))
            return true;

        return assignedValue is IConditionalOperation conditional &&
               (IsAssignedDelegateTarget(conditional.WhenTrue, delegateTarget) ||
                IsAssignedDelegateTarget(conditional.WhenFalse, delegateTarget));
    }

    private static bool IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
        IOperation assignmentOperation,
        IOperation saveOperation,
        IInvocationOperation invocation)
    {
        if (!TryGetConditionalDelegateAssignmentArm(
                assignmentOperation,
                saveOperation,
                out var conditional,
                out var saveRequiresConditionTrue))
        {
            return false;
        }

        if (!IsConditionStableBetween(conditional.Condition, assignmentOperation, invocation))
            return false;

        for (IOperation? current = invocation; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is IConditionalOperation branch &&
                TryGetBranchConditionPolarity(branch, invocation, conditional.Condition, out var invocationRequiresConditionTrue))
            {
                return invocationRequiresConditionTrue != saveRequiresConditionTrue;
            }

            if (current.Parent is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                break;
        }

        return false;
    }

    private static bool IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
        IOperation assignmentOperation,
        IInvocationOperation invocation)
    {
        return IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
            assignmentOperation,
            invocation,
            loop: null);
    }

    private static bool IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
        IOperation assignmentOperation,
        IInvocationOperation invocation,
        ILoopOperation? loop)
    {
        foreach (var assignmentGuard in GetEnclosingBranchConditions(assignmentOperation))
        {
            foreach (var invocationGuard in GetEnclosingBranchConditions(invocation))
            {
                if (AreBranchConditionsMutuallyExclusive(
                        assignmentGuard.Condition,
                        assignmentGuard.RequiresTrue,
                        invocationGuard.Condition,
                        invocationGuard.RequiresTrue) &&
                    IsBranchConditionStableForDelegateFlow(
                        assignmentGuard.Condition,
                        assignmentOperation,
                        invocation,
                        loop))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsBranchConditionStableForDelegateFlow(
        IOperation condition,
        IOperation assignmentOperation,
        IInvocationOperation invocation,
        ILoopOperation? loop)
    {
        if (assignmentOperation.Syntax.SpanStart < invocation.Syntax.SpanStart)
            return IsConditionStableBetween(condition, assignmentOperation, invocation);

        return loop != null &&
               IsConditionStableAcrossLoopIterations(condition, loop, assignmentOperation, invocation);
    }

    private static bool IsBranchExitGuardedAssignmentMutuallyExclusiveWithInvocation(
        IOperation assignmentOperation,
        IInvocationOperation invocation,
        ILoopOperation loop)
    {
        for (IOperation? current = assignmentOperation; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is IConditionalOperation branch &&
                ContainsOperation(loop, branch) &&
                !ContainsOperation(branch, invocation) &&
                branch.Syntax.Span.End <= invocation.Syntax.SpanStart &&
                TryGetOperationBranchPolarity(branch, assignmentOperation, out _) &&
                !CanFallThroughFromSourceToAncestorInCurrentIteration(assignmentOperation, branch) &&
                IsConditionStableAcrossLoopIterations(branch.Condition, loop, assignmentOperation, invocation))
            {
                return true;
            }

            if (current.Parent is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                break;
        }

        return false;
    }

    private static bool IsConditionStableBetween(
        IOperation condition,
        IOperation source,
        IOperation destination)
    {
        var containingRoot = source.FindOwningExecutableRoot();
        if (containingRoot == null ||
            !ReferenceEquals(destination.FindOwningExecutableRoot(), containingRoot))
        {
            return false;
        }

        var afterStart = source.Syntax.SpanStart;
        var beforeStart = destination.Syntax.SpanStart;
        foreach (var operation in GetOperationAndDescendants(condition))
        {
            switch (operation.UnwrapConversions())
            {
                case ILocalReferenceOperation localReference:
                    if (IsLocalPossiblyAssignedBetween(
                            containingRoot,
                            localReference.Local,
                            afterStart,
                            beforeStart,
                            destination))
                    {
                        return false;
                    }

                    break;

                case IParameterReferenceOperation parameterReference:
                    if (IsParameterPossiblyAssignedBetween(
                            containingRoot,
                            parameterReference.Parameter,
                            afterStart,
                            beforeStart,
                            destination))
                    {
                        return false;
                    }

                    break;

                case IFieldReferenceOperation
                    or IPropertyReferenceOperation
                    or IInvocationOperation
                    or ISimpleAssignmentOperation
                    or ICompoundAssignmentOperation
                    or IIncrementOrDecrementOperation:
                    return false;
            }
        }

        return true;
    }

    private static bool IsConditionStableAcrossLoopIterations(
        IOperation condition,
        ILoopOperation loop,
        IOperation source,
        IOperation destination)
    {
        if (!IsConditionStableBetween(condition, source, destination))
            return false;

        foreach (var operation in GetOperationAndDescendants(condition))
        {
            switch (operation.UnwrapConversions())
            {
                case ILocalReferenceOperation localReference:
                    if (IsLocalDeclaredInsideOperation(localReference.Local, loop) ||
                        IsLocalWrittenInsideRoot(loop, localReference.Local))
                    {
                        return false;
                    }

                    break;

                case IParameterReferenceOperation parameterReference:
                    if (IsParameterWrittenInsideRoot(loop, parameterReference.Parameter))
                        return false;

                    break;
            }
        }

        return true;
    }

    private static bool IsLocalDeclaredInsideOperation(ILocalSymbol local, IOperation operation)
    {
        return local.DeclaringSyntaxReferences.Any(reference =>
            operation.Syntax.Span.Contains(reference.Span));
    }

    private static IEnumerable<IOperation> GetOperationAndDescendants(IOperation operation)
    {
        yield return operation;

        foreach (var descendant in operation.Descendants())
        {
            yield return descendant;
        }
    }

    private static IEnumerable<(IOperation Condition, bool RequiresTrue)> GetEnclosingBranchConditions(
        IOperation operation)
    {
        for (IOperation? current = operation; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is IConditionalOperation branch &&
                TryGetOperationBranchPolarity(branch, operation, out var requiresTrue))
            {
                yield return (branch.Condition, requiresTrue);
            }

            if (current.Parent is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                yield break;
        }
    }

    private static bool TryGetOperationBranchPolarity(
        IConditionalOperation branch,
        IOperation operation,
        out bool requiresTrue)
    {
        if (ContainsOperation(branch.WhenTrue, operation))
        {
            requiresTrue = true;
            return true;
        }

        if (branch.WhenFalse != null &&
            ContainsOperation(branch.WhenFalse, operation))
        {
            requiresTrue = false;
            return true;
        }

        requiresTrue = false;
        return false;
    }

    private static bool AreBranchConditionsMutuallyExclusive(
        IOperation leftCondition,
        bool leftRequiresTrue,
        IOperation rightCondition,
        bool rightRequiresTrue)
    {
        if (IsSameCondition(leftCondition, rightCondition))
            return leftRequiresTrue != rightRequiresTrue;

        if (IsNegatedCondition(leftCondition, rightCondition) ||
            IsNegatedCondition(rightCondition, leftCondition))
        {
            return leftRequiresTrue == rightRequiresTrue;
        }

        return false;
    }

    private static bool TryGetConditionalDelegateAssignmentArm(
        IOperation assignmentOperation,
        IOperation saveOperation,
        out IConditionalOperation conditional,
        out bool saveRequiresConditionTrue)
    {
        foreach (var candidate in assignmentOperation.Descendants().OfType<IConditionalOperation>())
        {
            if (ContainsOperation(candidate.WhenTrue, saveOperation))
            {
                conditional = candidate;
                saveRequiresConditionTrue = true;
                return true;
            }

            if (candidate.WhenFalse != null &&
                ContainsOperation(candidate.WhenFalse, saveOperation))
            {
                conditional = candidate;
                saveRequiresConditionTrue = false;
                return true;
            }
        }

        conditional = null!;
        saveRequiresConditionTrue = false;
        return false;
    }

    private static bool TryGetBranchConditionPolarity(
        IConditionalOperation branch,
        IInvocationOperation invocation,
        IOperation delegateCondition,
        out bool invocationRequiresConditionTrue)
    {
        var invocationInWhenTrue = ContainsOperation(branch.WhenTrue, invocation);
        var invocationInWhenFalse = branch.WhenFalse != null &&
                                    ContainsOperation(branch.WhenFalse, invocation);
        if (!invocationInWhenTrue && !invocationInWhenFalse)
        {
            invocationRequiresConditionTrue = false;
            return false;
        }

        if (IsSameCondition(branch.Condition, delegateCondition))
        {
            invocationRequiresConditionTrue = invocationInWhenTrue;
            return true;
        }

        if (IsNegatedCondition(branch.Condition, delegateCondition))
        {
            invocationRequiresConditionTrue = !invocationInWhenTrue;
            return true;
        }

        invocationRequiresConditionTrue = false;
        return false;
    }

    private static bool IsNegatedCondition(IOperation maybeNegated, IOperation condition)
    {
        var value = maybeNegated.UnwrapConversions();
        if (value is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary)
            return IsSameCondition(unary.Operand, condition);

        return TryGetBooleanLiteralComparison(value, out var operand, out var requiresTrue) &&
               !requiresTrue &&
               IsSameCondition(operand, condition);
    }

    private static bool IsSameCondition(IOperation left, IOperation right)
    {
        if (TryGetBooleanLiteralComparison(left, out var leftOperand, out var leftRequiresTrue) &&
            leftRequiresTrue &&
            IsSameCondition(leftOperand, right))
        {
            return true;
        }

        if (TryGetBooleanLiteralComparison(right, out var rightOperand, out var rightRequiresTrue) &&
            rightRequiresTrue &&
            IsSameCondition(left, rightOperand))
        {
            return true;
        }

        return NormalizeCondition(left) == NormalizeCondition(right);
    }

    private static bool TryGetBooleanLiteralComparison(
        IOperation operation,
        out IOperation operand,
        out bool requiresTrue)
    {
        if (operation.UnwrapConversions() is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals,
            } binary)
        {
            operand = null!;
            requiresTrue = false;
            return false;
        }

        bool literal;
        if (TryGetBooleanLiteral(binary.LeftOperand, out literal))
        {
            operand = binary.RightOperand;
        }
        else if (TryGetBooleanLiteral(binary.RightOperand, out literal))
        {
            operand = binary.LeftOperand;
        }
        else
        {
            operand = null!;
            requiresTrue = false;
            return false;
        }

        requiresTrue = binary.OperatorKind == BinaryOperatorKind.Equals ? literal : !literal;
        return true;
    }

    private static bool TryGetBooleanLiteral(IOperation operation, out bool value)
    {
        if (operation.UnwrapConversions().ConstantValue is { HasValue: true, Value: bool constant })
        {
            value = constant;
            return true;
        }

        value = false;
        return false;
    }

    private static string NormalizeCondition(IOperation operation)
    {
        return new string(operation.UnwrapConversions()
            .Syntax
            .ToString()
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray());
    }

    private static bool ContainsOperation(IOperation root, IOperation descendant)
    {
        for (var current = descendant; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }

        return false;
    }

    private static bool IsLocalFunctionDelegateCalledFromLoop(
        IOperation containingRoot,
        IMethodSymbol localFunction,
        IInvocationOperation saveInvocation)
    {
        if (FindDelegateLocalAssignments(
                containingRoot,
                operation => IsLocalFunctionReference(operation, localFunction))
            .Any(assignment => IsDelegateLocalOrAliasesCalledFromLoop(
                containingRoot,
                assignment.Local,
                assignment.AssignmentStart,
                assignment.AssignmentOperation,
                saveInvocation)))
        {
            return true;
        }

        return containingRoot.Descendants()
            .OfType<ILocalFunctionOperation>()
            .Where(candidate => ReferenceEquals(FindContainingExecutableRoot(candidate), containingRoot))
            .SelectMany(assignmentFunction => FindDelegateLocalAssignments(
                    assignmentFunction,
                    operation => IsLocalFunctionReference(operation, localFunction))
                .Select(assignment => (AssignmentFunction: assignmentFunction, Assignment: assignment)))
            .Any(candidate => IsDelegateAssignedInsideCalledLocalFunctionCalledFromLoop(
                candidate.AssignmentFunction,
                candidate.Assignment.Local,
                candidate.Assignment.AssignmentStart,
                candidate.Assignment.AssignmentOperation,
                saveInvocation));
    }

    private static bool IsDelegateAssignedInsideCalledLocalFunctionCalledFromLoop(
        ILocalFunctionOperation assignmentFunction,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation)
    {
        var containingRoot = FindContainingExecutableRoot(assignmentFunction);
        if (containingRoot == null ||
            IsLocalAssignedAfterStartOnSamePath(assignmentFunction, local, assignmentStart, assignmentOperation))
        {
            return false;
        }

        foreach (var call in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (ReferenceEquals(call.FindOwningExecutableRoot(), containingRoot) &&
                SymbolEqualityComparer.Default.Equals(call.TargetMethod, assignmentFunction.Symbol) &&
                IsDelegateLocalOrAliasesCalledFromLoopAfterSetupCall(containingRoot, local, call, saveOperation))
            {
                return true;
            }
        }

        return IsDelegateAssignedInsideCalledLocalFunctionReachedThroughLiveLocalFunction(
            containingRoot,
            assignmentFunction,
            local,
            saveOperation);
    }

    private static bool IsDelegateAssignedInsideCalledLocalFunctionReachedThroughLiveLocalFunction(
        IOperation containingRoot,
        ILocalFunctionOperation assignmentFunction,
        ILocalSymbol local,
        IOperation saveOperation)
    {
        foreach (var callerFunction in containingRoot.Descendants().OfType<ILocalFunctionOperation>())
        {
            if (!ReferenceEquals(FindContainingExecutableRoot(callerFunction), containingRoot) ||
                !IsLocalFunctionCalledFromRoot(containingRoot, callerFunction.Symbol))
            {
                continue;
            }

            foreach (var setupCall in callerFunction.Descendants().OfType<IInvocationOperation>())
            {
                if (ReferenceEquals(setupCall.FindOwningExecutableRoot(), callerFunction) &&
                    SymbolEqualityComparer.Default.Equals(setupCall.TargetMethod, assignmentFunction.Symbol) &&
                    (IsDelegateLocalOrAliasesCalledFromLoopAfterSetupCall(callerFunction, local, setupCall, saveOperation) ||
                     IsDelegateAssignedBySetupWrapperCalledBeforeLoop(
                         containingRoot,
                         callerFunction,
                         local,
                         setupCall,
                         saveOperation)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDelegateAssignedBySetupWrapperCalledBeforeLoop(
        IOperation containingRoot,
        ILocalFunctionOperation callerFunction,
        ILocalSymbol local,
        IInvocationOperation setupCall,
        IOperation saveOperation)
    {
        if (IsLocalAssignedAfterStartOnSamePath(callerFunction, local, setupCall.Syntax.SpanStart, setupCall))
            return false;

        foreach (var wrapperCall in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (ReferenceEquals(wrapperCall.FindOwningExecutableRoot(), containingRoot) &&
                SymbolEqualityComparer.Default.Equals(wrapperCall.TargetMethod, callerFunction.Symbol) &&
                IsDelegateLocalOrAliasesCalledFromLoopAfterSetupCall(containingRoot, local, wrapperCall, saveOperation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLocalFunctionCalledFromRoot(IOperation containingRoot, IMethodSymbol localFunction)
    {
        return containingRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(call => ReferenceEquals(call.FindOwningExecutableRoot(), containingRoot) &&
                         SymbolEqualityComparer.Default.Equals(call.TargetMethod, localFunction));
    }

    private static bool IsDelegateLocalOrAliasesCalledFromLoopAfterSetupCall(
        IOperation containingRoot,
        ILocalSymbol local,
        IInvocationOperation setupCall,
        IOperation saveOperation,
        int depth = 0)
    {
        if (IsDelegateLocalCalledFromLoopAfterSetupCall(containingRoot, local, setupCall, saveOperation))
            return true;

        if (depth >= MaxDelegateCallChainDepth)
            return false;

        var setupCallStart = setupCall.Syntax.SpanStart;
        foreach (var alias in FindDelegateLocalAliasAssignmentsAfterSource(
                     containingRoot,
                     local,
                     setupCallStart,
                     setupCall))
        {
            if (IsDelegateLocalOrAliasesCalledFromLoop(
                    containingRoot,
                    alias.Local,
                    alias.AssignmentStart,
                    alias.AssignmentOperation,
                    saveOperation,
                    depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDelegateLocalCalledFromLoopAfterSetupCall(
        IOperation containingRoot,
        ILocalSymbol local,
        IInvocationOperation setupCall,
        IOperation saveOperation)
    {
        var setupCallStart = setupCall.Syntax.SpanStart;
        foreach (var invocation in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var invocationStart = invocation.Syntax.SpanStart;
            var loop = FindSaveExecutionLoop(invocation);
            if (!IsDelegateLocalInvocation(invocation, local) ||
                loop == null ||
                !ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) ||
                invocationStart <= setupCallStart ||
                !CanReachDestination(setupCall, invocation) ||
                IsLocalAssignedInStraightLinePathBetween(containingRoot, local, setupCallStart, invocationStart, invocation) ||
                IsSaveReceiverFreshContextForDirectDelegateInvocation(
                    saveOperation,
                    loop,
                    invocation))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static IEnumerable<(ILocalSymbol Local, int AssignmentStart, IOperation AssignmentOperation)>
        FindDelegateLocalAssignments(
        IOperation containingRoot,
        Func<IOperation, bool> isAssignedTarget)
    {
        foreach (var declarator in containingRoot.Descendants().OfType<IVariableDeclaratorOperation>())
        {
            if (ReferenceEquals(declarator.FindOwningExecutableRoot(), containingRoot) &&
                declarator.Initializer?.Value is { } value &&
                isAssignedTarget(value))
            {
                yield return (declarator.Symbol, declarator.Syntax.SpanStart, declarator);
            }
        }

        foreach (var assignment in containingRoot.Descendants().OfType<ISimpleAssignmentOperation>())
        {
            if (ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                isAssignedTarget(assignment.Value))
            {
                yield return (localReference.Local, assignment.Syntax.SpanStart, assignment);
            }
        }

        foreach (var assignment in containingRoot.Descendants().OfType<ICompoundAssignmentOperation>())
        {
            if (ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                assignment.OperatorKind == BinaryOperatorKind.Add &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                isAssignedTarget(assignment.Value))
            {
                yield return (localReference.Local, assignment.Syntax.SpanStart, assignment);
            }
        }
    }

    private static IEnumerable<(ILocalSymbol Local, int AssignmentStart, IOperation AssignmentOperation)>
        FindDelegateLocalAliasAssignmentsAfterSource(
            IOperation containingRoot,
            ILocalSymbol sourceLocal,
            int sourceStart,
            IOperation sourceOperation)
    {
        foreach (var declarator in containingRoot.Descendants().OfType<IVariableDeclaratorOperation>())
        {
            if (ReferenceEquals(declarator.FindOwningExecutableRoot(), containingRoot) &&
                declarator.Initializer?.Value is { } value &&
                IsAssignedDelegateLocal(value, sourceLocal) &&
                IsReachableDelegateAliasAssignment(containingRoot, sourceLocal, sourceStart, sourceOperation, declarator))
            {
                yield return (declarator.Symbol, declarator.Syntax.SpanStart, declarator);
            }
        }

        foreach (var assignment in containingRoot.Descendants().OfType<ISimpleAssignmentOperation>())
        {
            if (ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                IsAssignedDelegateLocal(assignment.Value, sourceLocal) &&
                IsReachableDelegateAliasAssignment(containingRoot, sourceLocal, sourceStart, sourceOperation, assignment))
            {
                yield return (localReference.Local, assignment.Syntax.SpanStart, assignment);
            }
        }

        foreach (var assignment in containingRoot.Descendants().OfType<ICompoundAssignmentOperation>())
        {
            if (ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                assignment.OperatorKind == BinaryOperatorKind.Add &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                IsAssignedDelegateLocal(assignment.Value, sourceLocal) &&
                IsReachableDelegateAliasAssignment(containingRoot, sourceLocal, sourceStart, sourceOperation, assignment))
            {
                yield return (localReference.Local, assignment.Syntax.SpanStart, assignment);
            }
        }
    }

    private static bool IsAssignedDelegateLocal(IOperation? value, ILocalSymbol sourceLocal)
    {
        if (value == null)
            return false;

        var assignedValue = value.UnwrapConversions();
        if (assignedValue is IDelegateCreationOperation delegateCreation)
            assignedValue = delegateCreation.Target.UnwrapConversions();

        if (IsLocalReference(assignedValue, sourceLocal))
            return true;

        return assignedValue is IConditionalOperation conditional &&
               (IsAssignedDelegateLocal(conditional.WhenTrue, sourceLocal) ||
                IsAssignedDelegateLocal(conditional.WhenFalse, sourceLocal));
    }

    private static bool IsReachableDelegateAliasAssignment(
        IOperation containingRoot,
        ILocalSymbol sourceLocal,
        int sourceStart,
        IOperation sourceOperation,
        IOperation aliasAssignment)
    {
        var aliasStart = aliasAssignment.Syntax.SpanStart;
        return aliasStart > sourceStart &&
               CanReachDestination(sourceOperation, aliasAssignment) &&
               !IsLocalAssignedInStraightLinePathBetween(
                   containingRoot,
                   sourceLocal,
                   sourceStart,
                   aliasStart,
                   aliasAssignment,
                   sourceOperation);
    }

    private static bool IsDelegateLocalOrAliasesCalledFromLoop(
        IOperation containingRoot,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation,
        int depth = 0)
    {
        if (IsDelegateLocalCalledFromLoop(
                containingRoot,
                local,
                assignmentStart,
                assignmentOperation,
                saveOperation))
        {
            return true;
        }

        if (depth >= MaxDelegateCallChainDepth)
            return false;

        foreach (var alias in FindDelegateLocalAliasAssignmentsAfterSource(
                     containingRoot,
                     local,
                     assignmentStart,
                     assignmentOperation))
        {
            if (IsDelegateLocalOrAliasesCalledFromLoop(
                    containingRoot,
                    alias.Local,
                    alias.AssignmentStart,
                    alias.AssignmentOperation,
                    saveOperation,
                    depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDelegateLocalCalledFromLoop(
        IOperation containingRoot,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation)
    {
        foreach (var invocation in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var invocationStart = invocation.Syntax.SpanStart;
            if (!IsDelegateLocalInvocation(invocation, local))
            {
                if (IsDelegateLocalPassedToLocalInvokerCalledFromLoop(
                        containingRoot,
                        invocation,
                        local,
                        assignmentStart,
                        assignmentOperation,
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            var loop = FindSaveExecutionLoop(invocation);
            var invocationRoot = invocation.FindOwningExecutableRoot();
            if (invocationRoot is IAnonymousFunctionOperation)
            {
                if (IsDelegateInvocationInsideAnonymousFunctionCalledAfterAssignment(
                        containingRoot,
                        invocation,
                        local,
                        assignmentStart,
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            if (loop == null)
            {
                if (invocationRoot is ILocalFunctionOperation localFunctionWithoutLoop &&
                    IsLocalFunctionCalledFromLoopAfterDelegateAssignment(
                        containingRoot,
                        localFunctionWithoutLoop,
                        invocation,
                        local,
                        assignmentStart,
                        assignmentOperation,
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            if (IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    saveOperation,
                    invocation) ||
                IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    invocation,
                    loop) ||
                IsBranchExitGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    invocation,
                    loop))
            {
                continue;
            }

            if (invocationRoot is ILocalFunctionOperation)
            {
                if (IsDelegateInvocationInsideLocalFunctionCalledAfterAssignment(
                        containingRoot,
                        invocation,
                        local,
                        assignmentStart,
                        assignmentOperation,
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            if (invocationStart <= assignmentStart)
            {
                if (IsLoopCarriedDelegateAssignmentReachable(
                        containingRoot,
                        loop,
                        invocation,
                        local,
                        assignmentStart,
                        assignmentOperation))
                {
                    return true;
                }

                continue;
            }

            if (IsSaveReceiverFreshContextForDirectDelegateInvocation(
                    saveOperation,
                    loop,
                    invocation) &&
                IsFreshContextDelegateAssignmentIterationStable(
                    local,
                    assignmentOperation,
                    invocation,
                    loop))
            {
                continue;
            }

            if (CanReachDestination(assignmentOperation, invocation) &&
                !IsLocalAssignedInStraightLinePathBetween(
                    containingRoot,
                    local,
                    assignmentStart,
                    invocationStart,
                    invocation,
                    assignmentOperation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDelegateLocalPassedToLocalInvokerCalledFromLoop(
        IOperation containingRoot,
        IInvocationOperation invocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation)
    {
        var invocationStart = invocation.Syntax.SpanStart;
        if (!ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) ||
            !TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) ||
            TryFindDelegateLocalArgumentParameter(invocation, local) is not { } parameter)
        {
            return false;
        }

        var loop = FindSaveExecutionLoop(invocation);
        if (invocationStart > assignmentStart)
        {
            if (IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    saveOperation,
                    invocation) ||
                IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    invocation) ||
                !CanReachDestination(assignmentOperation, invocation) ||
                IsLocalAssignedInStraightLinePathBetween(
                    containingRoot,
                    local,
                    assignmentStart,
                    invocationStart,
                    invocation,
                    assignmentOperation))
            {
                return false;
            }
        }
        else
        {
            if (loop == null ||
                !ContainsOperation(loop, assignmentOperation) ||
                IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    saveOperation,
                    invocation) ||
                IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    invocation,
                    loop) ||
                IsBranchExitGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    invocation,
                    loop) ||
                !(CanReachDestination(invocation, assignmentOperation) ||
                  IsDelegateAssignmentReachableFromAlternativeLoopBranch(assignmentOperation, invocation, loop)) ||
                !CanReachNextLoopIteration(assignmentOperation, loop) ||
                IsDelegateLocalClearedBeforeLoopContinues(
                    containingRoot,
                    loop,
                    local,
                    assignmentStart,
                    assignmentOperation))
            {
                return false;
            }
        }

        foreach (var parameterInvocation in FindLocalFunctionParameterDelegateInvocations(localFunction, parameter))
        {
            var executionLoop = loop;
            var executionInvocation = invocation;
            if (executionLoop == null)
            {
                executionLoop = FindSaveExecutionLoop(parameterInvocation);
                executionInvocation = parameterInvocation;
            }

            if (executionLoop == null ||
                IsParameterPossiblyAssignedBetween(
                    localFunction,
                    parameter,
                    -1,
                    parameterInvocation.Syntax.SpanStart,
                    parameterInvocation) ||
                IsSaveReceiverFreshContextDeclaredInsideLoopBody(
                    saveOperation,
                    executionLoop,
                    GetFreshContextExecutionOperation(saveOperation, executionInvocation)) ||
                IsSaveReceiverFreshContextPassedThroughDelegateInvocation(
                    saveOperation,
                    executionLoop,
                    parameterInvocation,
                    invocation))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static IParameterSymbol? TryFindDelegateLocalArgumentParameter(
        IInvocationOperation invocation,
        ILocalSymbol local)
    {
        for (var argumentIndex = 0; argumentIndex < invocation.Arguments.Length; argumentIndex++)
        {
            var argument = invocation.Arguments[argumentIndex];
            if (!IsLocalReference(argument.Value, local))
                continue;

            return argument.Parameter ??
                   (argumentIndex < invocation.TargetMethod.Parameters.Length
                       ? invocation.TargetMethod.Parameters[argumentIndex]
                       : null);
        }

        return null;
    }

    private static IEnumerable<IInvocationOperation> FindLocalFunctionParameterDelegateInvocations(
        ILocalFunctionOperation localFunction,
        IParameterSymbol parameter)
    {
        foreach (var invocation in localFunction.Descendants().OfType<IInvocationOperation>())
        {
            if (ReferenceEquals(invocation.FindOwningExecutableRoot(), localFunction) &&
                IsDelegateParameterInvocation(invocation, parameter))
            {
                yield return invocation;
            }
        }
    }

    private static bool IsDelegateParameterInvocation(IInvocationOperation invocation, IParameterSymbol parameter)
    {
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke)
            return false;

        return invocation.GetInvocationReceiver() is { } receiver && IsParameterReference(receiver, parameter) ||
               IsConditionalAccessDelegateParameterInvocation(invocation, parameter);
    }

    private static bool IsConditionalAccessDelegateParameterInvocation(
        IInvocationOperation invocation,
        IParameterSymbol parameter)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is IConditionalAccessOperation conditionalAccess)
                return IsParameterReference(conditionalAccess.Operation, parameter);

            if (current is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return false;
        }

        return false;
    }

    private static bool IsDelegateInvocationInsideAnonymousFunctionCalledAfterAssignment(
        IOperation assignmentRoot,
        IInvocationOperation delegateInvocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation saveOperation,
        int depth = 0,
        List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>? capturedLocals = null)
    {
        var anonymousFunction = FindDirectOwningAnonymousFunction(delegateInvocation);
        if (anonymousFunction == null)
            return false;

        var containingRoot = FindContainingExecutableRootForDelegateTarget(anonymousFunction);
        if (containingRoot == null ||
            !TryGetDelegateLocalAssignment(
                anonymousFunction,
                out var anonymousLocal,
                out var anonymousAssignmentStart,
                out var anonymousAssignmentOperation))
        {
            return false;
        }

        foreach (var call in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var callStart = call.Syntax.SpanStart;
            var callRoot = call.FindOwningExecutableRoot();
            if (callStart <= anonymousAssignmentStart ||
                !IsDelegateLocalInvocation(call, anonymousLocal) ||
                IsLocalAssignedInStraightLinePathBetween(containingRoot, anonymousLocal, anonymousAssignmentStart, callStart, call))
            {
                continue;
            }

            if (!ReferenceEquals(callRoot, containingRoot))
            {
                var nextCapturedLocals = AddCapturedDelegateLocalIfNeeded(
                    capturedLocals,
                    assignmentRoot,
                    containingRoot,
                    local,
                    assignmentStart);

                if (callRoot is IAnonymousFunctionOperation &&
                    depth < MaxDelegateCallChainDepth &&
                    IsCapturedDelegateLocalStableForNestedWrapper(
                        assignmentRoot,
                        containingRoot,
                        anonymousFunction,
                        local,
                        assignmentStart,
                        delegateInvocation) &&
                    IsDelegateInvocationInsideAnonymousFunctionCalledAfterAssignment(
                        containingRoot,
                        call,
                        anonymousLocal,
                        anonymousAssignmentStart,
                        saveOperation,
                        depth + 1,
                        nextCapturedLocals))
                {
                    return true;
                }

                continue;
            }

            var loop = FindOuterDelegateExecutionLoop(call, delegateInvocation);
            if (loop == null ||
                IsSaveReceiverFreshContextDeclaredInsideLoopBody(
                    saveOperation,
                    loop,
                    GetFreshContextExecutionOperation(saveOperation, call)) ||
                IsSaveReceiverFreshContextPassedThroughDelegateInvocation(
                    saveOperation,
                    loop,
                    delegateInvocation,
                    call))
            {
                continue;
            }

            if (ReferenceEquals(assignmentRoot, containingRoot) &&
                callStart > assignmentStart &&
                CanReachDestination(anonymousAssignmentOperation, call) &&
                AreCapturedDelegateLocalsStable(capturedLocals, callStart, call) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, callStart, call) &&
                !IsLocalAssignedBeforeDestinationInRoot(anonymousFunction, local, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, anonymousFunction) &&
                delegateInvocation.Syntax.SpanStart > assignmentStart &&
                CanReachDestination(anonymousAssignmentOperation, call) &&
                AreCapturedDelegateLocalsStable(capturedLocals, callStart, call) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, delegateInvocation.Syntax.SpanStart, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, anonymousFunction) &&
                AreCapturedDelegateLocalsStable(capturedLocals, callStart, call) &&
                IsLoopCarriedWrapperDelegateAssignmentReachable(
                    anonymousFunction,
                    delegateInvocation,
                    local,
                    assignmentStart,
                    anonymousAssignmentOperation,
                    call,
                    loop))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCapturedDelegateLocalStableForNestedWrapper(
        IOperation assignmentRoot,
        IOperation containingRoot,
        IAnonymousFunctionOperation anonymousFunction,
        ILocalSymbol local,
        int assignmentStart,
        IInvocationOperation delegateInvocation)
    {
        if (ReferenceEquals(assignmentRoot, containingRoot))
        {
            return !IsLocalAssignedBeforeDestinationInRoot(anonymousFunction, local, delegateInvocation);
        }

        return ReferenceEquals(assignmentRoot, anonymousFunction) &&
               delegateInvocation.Syntax.SpanStart > assignmentStart &&
               !IsLocalAssignedInStraightLinePathBetween(
                   assignmentRoot,
                   local,
                   assignmentStart,
                   delegateInvocation.Syntax.SpanStart,
                   delegateInvocation);
    }

    private static List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>?
        AddCapturedDelegateLocalIfNeeded(
            List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>? capturedLocals,
            IOperation assignmentRoot,
            IOperation containingRoot,
            ILocalSymbol local,
            int assignmentStart)
    {
        if (!ReferenceEquals(assignmentRoot, containingRoot))
            return capturedLocals;

        var nextCapturedLocals = capturedLocals == null
            ? new List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>()
            : new List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>(capturedLocals);

        nextCapturedLocals.Add((assignmentRoot, local, assignmentStart));
        return nextCapturedLocals;
    }

    private static bool AreCapturedDelegateLocalsStable(
        List<(IOperation AssignmentRoot, ILocalSymbol Local, int AssignmentStart)>? capturedLocals,
        int beforeStart,
        IOperation destination)
    {
        return capturedLocals == null ||
               capturedLocals.All(captured =>
                   !IsLocalAssignedInStraightLinePathBetween(
                       captured.AssignmentRoot,
                       captured.Local,
                       captured.AssignmentStart,
                       beforeStart,
                       destination));
    }

    private static ILoopOperation? FindOuterDelegateExecutionLoop(
        IInvocationOperation outerDelegateCall,
        IInvocationOperation innerDelegateInvocation)
    {
        var innerLoop = FindSaveExecutionLoop(innerDelegateInvocation);
        if (innerLoop != null)
            return innerLoop;

        return FindSaveExecutionLoop(outerDelegateCall);
    }

    private static bool IsDelegateInvocationInsideLocalFunctionCalledAfterAssignment(
        IOperation assignmentRoot,
        IInvocationOperation delegateInvocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation)
    {
        var localFunction = FindDirectOwningLocalFunction(delegateInvocation);
        if (localFunction == null)
            return false;

        var containingRoot = FindContainingExecutableRoot(localFunction);
        if (containingRoot == null)
            return false;

        foreach (var call in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var callStart = call.Syntax.SpanStart;
            if (!ReferenceEquals(call.FindOwningExecutableRoot(), containingRoot) ||
                !SymbolEqualityComparer.Default.Equals(call.TargetMethod, localFunction.Symbol))
            {
                continue;
            }

            if (ReferenceEquals(assignmentRoot, containingRoot) &&
                callStart > assignmentStart &&
                CanReachDestination(assignmentOperation, call) &&
                !IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    saveOperation,
                    call) &&
                !IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    call) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, callStart, call) &&
                !IsLocalAssignedBeforeDestinationInRoot(localFunction, local, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, localFunction) &&
                delegateInvocation.Syntax.SpanStart > assignmentStart &&
                CanReachDestination(assignmentOperation, delegateInvocation) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, delegateInvocation.Syntax.SpanStart, delegateInvocation))
            {
                return true;
            }

            var loop = FindSaveExecutionLoop(call);
            if (ReferenceEquals(assignmentRoot, localFunction) &&
                loop != null &&
                IsLoopCarriedWrapperDelegateAssignmentReachable(
                    localFunction,
                    delegateInvocation,
                    local,
                    assignmentStart,
                    assignmentOperation,
                    call,
                    loop))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLocalFunctionCalledFromLoopAfterDelegateAssignment(
        IOperation assignmentRoot,
        ILocalFunctionOperation localFunction,
        IInvocationOperation delegateInvocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IOperation saveOperation)
    {
        var containingRoot = FindContainingExecutableRoot(localFunction);
        if (containingRoot == null)
            return false;

        foreach (var call in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var loop = FindSaveExecutionLoop(call);
            if (loop == null ||
                !ReferenceEquals(call.FindOwningExecutableRoot(), containingRoot) ||
                !SymbolEqualityComparer.Default.Equals(call.TargetMethod, localFunction.Symbol) ||
                IsSaveReceiverFreshContextDeclaredInsideLoopBody(
                    saveOperation,
                    loop,
                    GetFreshContextExecutionOperation(saveOperation, call)))
            {
                continue;
            }

            var callStart = call.Syntax.SpanStart;
            if (ReferenceEquals(assignmentRoot, containingRoot) &&
                callStart > assignmentStart &&
                CanReachDestination(assignmentOperation, call) &&
                !IsConditionalDelegateAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    saveOperation,
                    call) &&
                !IsBranchGuardedAssignmentMutuallyExclusiveWithInvocation(
                    assignmentOperation,
                    call) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, callStart, call) &&
                !IsLocalAssignedBeforeDestinationInRoot(localFunction, local, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, localFunction) &&
                delegateInvocation.Syntax.SpanStart > assignmentStart &&
                CanReachDestination(assignmentOperation, delegateInvocation) &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, delegateInvocation.Syntax.SpanStart, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, localFunction) &&
                IsLoopCarriedWrapperDelegateAssignmentReachable(
                    localFunction,
                    delegateInvocation,
                    local,
                    assignmentStart,
                    assignmentOperation,
                    call,
                    loop))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoopCarriedWrapperDelegateAssignmentReachable(
        IOperation wrapperRoot,
        IInvocationOperation delegateInvocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation,
        IInvocationOperation wrapperCall,
        ILoopOperation loop)
    {
        return ContainsOperation(loop, wrapperCall) &&
               delegateInvocation.Syntax.SpanStart <= assignmentStart &&
               CanReachDestination(delegateInvocation, assignmentOperation) &&
               CanReachNextLoopIteration(wrapperCall, loop) &&
               !IsLocalAssignedAfterStartOnSamePath(
                   wrapperRoot,
                   local,
                   assignmentStart,
                   assignmentOperation);
    }

    private static bool IsLocalFunctionReference(IOperation operation, IMethodSymbol localFunction)
    {
        var value = operation.UnwrapConversions();
        if (value is IDelegateCreationOperation delegateCreation)
            value = delegateCreation.Target.UnwrapConversions();

        if (value is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary)
        {
            return IsLocalFunctionReference(binary.LeftOperand, localFunction) ||
                   IsLocalFunctionReference(binary.RightOperand, localFunction);
        }

        if (value is IMethodReferenceOperation methodReference &&
            SymbolEqualityComparer.Default.Equals(methodReference.Method, localFunction))
        {
            return true;
        }

        return value is IConditionalOperation conditional &&
               (IsLocalFunctionReference(conditional.WhenTrue, localFunction) ||
                conditional.WhenFalse is { } whenFalse &&
                IsLocalFunctionReference(whenFalse, localFunction));
    }

    private static bool IsDelegateLocalInvocation(IInvocationOperation invocation, ILocalSymbol local)
    {
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke)
            return false;

        return invocation.GetInvocationReceiver() is { } receiver && IsLocalReference(receiver, local) ||
               IsConditionalAccessDelegateLocalInvocation(invocation, local);
    }

    private static bool IsConditionalAccessDelegateLocalInvocation(IInvocationOperation invocation, ILocalSymbol local)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is IConditionalAccessOperation conditionalAccess)
                return IsLocalReference(conditionalAccess.Operation, local);

            if (current is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return false;
        }

        return false;
    }

    private static IOperation GetFreshContextExecutionOperation(IOperation saveOperation, IInvocationOperation delegateInvocation)
    {
        return saveOperation is IMethodReferenceOperation ? saveOperation : delegateInvocation;
    }

    private static bool IsFreshContextDelegateAssignmentIterationStable(
        ILocalSymbol local,
        IOperation assignmentOperation,
        IInvocationOperation invocation,
        ILoopOperation loop)
    {
        return !ContainsOperation(loop, assignmentOperation) ||
               IsLocalDeclaredInsideOperation(local, loop) ||
               IsInSameStraightLinePath(assignmentOperation, invocation);
    }

    private static ILoopOperation? FindSaveExecutionLoop(IOperation operation)
    {
        var loop = operation.FindEnclosingLoop();
        if (loop == null || !operation.SharesOwningExecutableRoot(loop))
            return null;

        return GetCatchGuardedRetrySuccessExitKind(operation, loop) switch
        {
            RetrySuccessExitKind.None => loop,
            RetrySuccessExitKind.Return => null,
            _ => FindEnclosingLoopOutsideSameRoot(operation, loop),
        };
    }

    private static ILoopOperation? FindEnclosingLoopOutsideSameRoot(IOperation operation, ILoopOperation currentLoop)
    {
        for (var current = currentLoop.Parent; current != null; current = current.Parent)
        {
            if (current is ILoopOperation loop)
                return operation.SharesOwningExecutableRoot(loop) ? loop : null;

            if (current is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return null;
        }

        return null;
    }

    private static bool IsLoopCarriedDelegateAssignmentReachable(
        IOperation containingRoot,
        ILoopOperation loop,
        IInvocationOperation invocation,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation)
    {
        return ReferenceEquals(assignmentOperation.FindOwningExecutableRoot(), containingRoot) &&
               ContainsOperation(loop, assignmentOperation) &&
               (CanReachDestination(invocation, assignmentOperation) ||
                IsDelegateAssignmentReachableFromAlternativeLoopBranch(assignmentOperation, invocation, loop)) &&
               CanReachNextLoopIteration(assignmentOperation, loop) &&
               !IsDelegateLocalClearedBeforeLoopContinues(
                   containingRoot,
                   loop,
                   local,
                   assignmentStart,
                   assignmentOperation);
    }

    private static bool IsDelegateAssignmentReachableFromAlternativeLoopBranch(
        IOperation assignmentOperation,
        IInvocationOperation invocation,
        ILoopOperation loop)
    {
        foreach (var assignmentGuard in GetEnclosingBranchConditions(assignmentOperation))
        {
            foreach (var invocationGuard in GetEnclosingBranchConditions(invocation))
            {
                if (ContainsOperation(loop, assignmentGuard.Condition) &&
                    ContainsOperation(loop, invocationGuard.Condition) &&
                    AreBranchConditionsMutuallyExclusive(
                        assignmentGuard.Condition,
                        assignmentGuard.RequiresTrue,
                        invocationGuard.Condition,
                        invocationGuard.RequiresTrue))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDelegateLocalClearedBeforeLoopContinues(
        IOperation containingRoot,
        ILoopOperation loop,
        ILocalSymbol local,
        int assignmentStart,
        IOperation assignmentOperation)
    {
        var loopEnd = loop.Syntax.Span.End;
        return containingRoot.Descendants()
                   .OfType<ISimpleAssignmentOperation>()
                   .Any(assignment => assignment.Syntax.SpanStart > assignmentStart &&
                                      assignment.Syntax.SpanStart < loopEnd &&
                                      ContainsOperation(loop, assignment) &&
                                      ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                                      assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                                      SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                                      !IsSelfPreservingDelegateAssignment(assignment, local) &&
                                      IsInSameStraightLinePath(assignment, assignmentOperation) &&
                                      CanReachNextLoopIteration(assignment, loop)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > assignmentStart &&
                                    argument.Syntax.SpanStart < loopEnd &&
                                    ContainsOperation(loop, argument) &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    IsInSameStraightLinePath(argument, assignmentOperation) &&
                                    CanReachNextLoopIteration(argument, loop)) ||
               containingRoot.Descendants()
                   .OfType<IInvocationOperation>()
                   .Any(invocation => invocation.Syntax.SpanStart > assignmentStart &&
                                      invocation.Syntax.SpanStart < loopEnd &&
                                      ContainsOperation(loop, invocation) &&
                                      ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) &&
                                      IsInSameStraightLinePath(invocation, assignmentOperation) &&
                                      CanReachDestination(assignmentOperation, invocation) &&
                                      CanReachNextLoopIteration(invocation, loop) &&
                                      TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) &&
                                      IsLocalWrittenInsideRoot(localFunction, local)) ||
               IsDelegateAssignmentFullyRemovedBeforeDestination(
                   containingRoot,
                   local,
                   assignmentStart,
                   loopEnd,
                   loop,
                   assignmentOperation);
    }

    private static bool IsLocalAssignedInStraightLinePathBetween(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination,
        IOperation? sourceAssignmentOperation = null)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => assignment.Syntax.SpanStart > afterStart &&
                               assignment.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                               SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                               !IsSelfPreservingDelegateAssignment(assignment, local) &&
                               IsInSameStraightLinePath(assignment, destination)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > afterStart &&
                                    argument.Syntax.SpanStart < beforeStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    IsInSameStraightLinePath(argument, destination)) ||
               containingRoot.Descendants()
                   .OfType<ICompoundAssignmentOperation>()
                   .Where(assignment => IsDefiniteDelegateCompoundAssignment(
                       assignment,
                       local,
                       afterStart,
                       beforeStart,
                       destination))
                   .Any(assignment => IsMatchingDelegateRemoval(
                       assignment,
                       sourceAssignmentOperation) &&
                       IsDelegateAssignmentFullyRemovedBeforeDestination(
                           containingRoot,
                           local,
                           afterStart,
                           beforeStart,
                           destination,
                           sourceAssignmentOperation)) ||
               IsLocalAssignedByCalledLocalFunctionInStraightLinePathBetween(
                   containingRoot,
                   local,
                   afterStart,
                   beforeStart,
                   destination);
    }

    private static bool IsSelfPreservingDelegateAssignment(ISimpleAssignmentOperation assignment, ILocalSymbol local)
    {
        return GetOperationAndDescendants(assignment.Value)
            .Any(operation => IsLocalReference(operation, local));
    }

    private static bool IsMatchingDelegateRemoval(
        ICompoundAssignmentOperation assignment,
        IOperation? sourceAssignmentOperation)
    {
        return sourceAssignmentOperation != null &&
               assignment.OperatorKind == BinaryOperatorKind.Subtract &&
               IsSameAssignedDelegateTarget(
                   GetDelegateAssignmentValue(sourceAssignmentOperation),
                   assignment.Value);
    }

    private static bool IsDelegateAssignmentFullyRemovedBeforeDestination(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination,
        IOperation? sourceAssignmentOperation)
    {
        if (sourceAssignmentOperation == null ||
            GetDelegateAssignmentValue(sourceAssignmentOperation) is not { } sourceValue)
        {
            return false;
        }

        var matchingAdditions = 1 + containingRoot.Descendants()
            .OfType<ICompoundAssignmentOperation>()
            .Count(assignment => IsReachableDelegateCompoundAssignment(
                                     assignment,
                                     local,
                                     afterStart,
                                     beforeStart,
                                     destination) &&
                                 assignment.OperatorKind == BinaryOperatorKind.Add &&
                                 IsSameAssignedDelegateTarget(sourceValue, assignment.Value));

        var matchingRemovals = containingRoot.Descendants()
            .OfType<ICompoundAssignmentOperation>()
            .Count(assignment => IsDefiniteDelegateCompoundAssignment(
                                     assignment,
                                     local,
                                     afterStart,
                                     beforeStart,
                                     destination) &&
                                 assignment.OperatorKind == BinaryOperatorKind.Subtract &&
                                 IsSameAssignedDelegateTarget(sourceValue, assignment.Value));

        return matchingRemovals >= matchingAdditions;
    }

    private static bool IsDefiniteDelegateCompoundAssignment(
        ICompoundAssignmentOperation assignment,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return IsDelegateCompoundAssignmentInRange(assignment, local, afterStart, beforeStart, destination) &&
               IsInSameStraightLinePath(assignment, destination);
    }

    private static bool IsReachableDelegateCompoundAssignment(
        ICompoundAssignmentOperation assignment,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return IsDelegateCompoundAssignmentInRange(assignment, local, afterStart, beforeStart, destination) &&
               CanReachDestination(assignment, destination);
    }

    private static bool IsDelegateCompoundAssignmentInRange(
        ICompoundAssignmentOperation assignment,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return assignment.Syntax.SpanStart > afterStart &&
               assignment.Syntax.SpanStart < beforeStart &&
               assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
               ReferenceEquals(assignment.FindOwningExecutableRoot(), destination.FindOwningExecutableRoot());
    }

    private static IOperation? GetDelegateAssignmentValue(IOperation assignmentOperation)
    {
        return assignmentOperation switch
        {
            IVariableDeclaratorOperation declarator => declarator.Initializer?.Value,
            ISimpleAssignmentOperation assignment => assignment.Value,
            ICompoundAssignmentOperation assignment when assignment.OperatorKind == BinaryOperatorKind.Add => assignment.Value,
            _ => null,
        };
    }

    private static bool IsSameAssignedDelegateTarget(IOperation? source, IOperation? candidate)
    {
        if (source == null || candidate == null)
            return false;

        var sourceValue = UnwrapAssignedDelegateTarget(source);
        var candidateValue = UnwrapAssignedDelegateTarget(candidate);

        if (sourceValue is IConditionalOperation sourceConditional)
        {
            return IsSameAssignedDelegateTarget(sourceConditional.WhenTrue, candidateValue) ||
                   sourceConditional.WhenFalse is { } sourceWhenFalse &&
                   IsSameAssignedDelegateTarget(sourceWhenFalse, candidateValue);
        }

        if (candidateValue is IConditionalOperation candidateConditional)
        {
            return IsSameAssignedDelegateTarget(sourceValue, candidateConditional.WhenTrue) ||
                   candidateConditional.WhenFalse is { } candidateWhenFalse &&
                   IsSameAssignedDelegateTarget(sourceValue, candidateWhenFalse);
        }

        if (ReferenceEquals(sourceValue, candidateValue))
            return true;

        if (sourceValue is IMethodReferenceOperation sourceMethod &&
            candidateValue is IMethodReferenceOperation candidateMethod)
        {
            return SymbolEqualityComparer.Default.Equals(sourceMethod.Method, candidateMethod.Method) &&
                   IsSameDelegateReceiver(sourceMethod.Instance, candidateMethod.Instance);
        }

        return sourceValue is ILocalReferenceOperation sourceLocal &&
               candidateValue is ILocalReferenceOperation candidateLocal &&
               SymbolEqualityComparer.Default.Equals(sourceLocal.Local, candidateLocal.Local);
    }

    private static IOperation UnwrapAssignedDelegateTarget(IOperation operation)
    {
        var value = operation.UnwrapConversions();
        return value is IDelegateCreationOperation delegateCreation
            ? delegateCreation.Target.UnwrapConversions()
            : value;
    }

    private static bool IsSameDelegateReceiver(IOperation? source, IOperation? candidate)
    {
        if (source == null || candidate == null)
            return source == null && candidate == null;

        var sourceValue = source.UnwrapConversions();
        var candidateValue = candidate.UnwrapConversions();

        if (sourceValue is ILocalReferenceOperation sourceLocal &&
            candidateValue is ILocalReferenceOperation candidateLocal)
        {
            return SymbolEqualityComparer.Default.Equals(sourceLocal.Local, candidateLocal.Local);
        }

        if (sourceValue is IParameterReferenceOperation sourceParameter &&
            candidateValue is IParameterReferenceOperation candidateParameter)
        {
            return SymbolEqualityComparer.Default.Equals(sourceParameter.Parameter, candidateParameter.Parameter);
        }

        return NormalizeCondition(sourceValue) == NormalizeCondition(candidateValue);
    }

    private static bool IsLocalAssignedBeforeDestinationInRoot(
        IOperation containingRoot,
        ILocalSymbol local,
        IOperation destination)
    {
        var beforeStart = destination.Syntax.SpanStart;
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => assignment.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                               SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                               IsInSameStraightLinePath(assignment, destination)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart < beforeStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    IsInSameStraightLinePath(argument, destination)) ||
               IsLocalAssignedByCalledLocalFunctionInStraightLinePathBetween(
                   containingRoot,
                   local,
                   -1,
                   beforeStart,
                   destination);
    }

    private static bool IsLocalPossiblyAssignedBetween(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => assignment.Syntax.SpanStart > afterStart &&
                               assignment.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                               SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                               CanReachDestination(assignment, destination)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > afterStart &&
                                    argument.Syntax.SpanStart < beforeStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    CanReachDestination(argument, destination)) ||
               IsLocalPossiblyAssignedByCalledLocalFunctionBetween(
                   containingRoot,
                   local,
                   afterStart,
                   beforeStart,
                   destination);
    }

    private static bool IsParameterPossiblyAssignedBetween(
        IOperation containingRoot,
        IParameterSymbol parameter,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => assignment.Syntax.SpanStart > afterStart &&
                               assignment.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               assignment.Target.UnwrapConversions() is IParameterReferenceOperation parameterReference &&
                               SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter) &&
                               CanReachDestination(assignment, destination)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > afterStart &&
                                    argument.Syntax.SpanStart < beforeStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsParameterReference(argument.Value, parameter) &&
                                    CanReachDestination(argument, destination)) ||
               IsParameterPossiblyAssignedByCalledLocalFunctionBetween(
                   containingRoot,
                   parameter,
                   afterStart,
                   beforeStart,
                   destination);
    }

    private static bool IsLocalPossiblyAssignedByCalledLocalFunctionBetween(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return containingRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart > afterStart &&
                               invocation.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) &&
                               CanReachDestination(invocation, destination) &&
                               TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) &&
                               IsLocalWrittenInsideRoot(localFunction, local));
    }

    private static bool IsParameterPossiblyAssignedByCalledLocalFunctionBetween(
        IOperation containingRoot,
        IParameterSymbol parameter,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return containingRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart > afterStart &&
                               invocation.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) &&
                               CanReachDestination(invocation, destination) &&
                               TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) &&
                               IsParameterWrittenInsideRoot(localFunction, parameter));
    }

    private static bool IsLocalAssignedAfterStartOnSamePath(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        IOperation source)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => assignment.Syntax.SpanStart > afterStart &&
                               ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                               SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                               IsInSameStraightLinePath(assignment, source)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > afterStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    IsInSameStraightLinePath(argument, source)) ||
               IsLocalAssignedByCalledLocalFunctionAfterStartOnSamePath(
                   containingRoot,
                   local,
                   afterStart,
                   source);
    }

    private static bool IsLocalAssignedByCalledLocalFunctionInStraightLinePathBetween(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        int beforeStart,
        IOperation destination)
    {
        return containingRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart > afterStart &&
                               invocation.Syntax.SpanStart < beforeStart &&
                               ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) &&
                               IsInSameStraightLinePath(invocation, destination) &&
                               CanReachDestination(invocation, destination) &&
                               TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) &&
                               IsLocalWrittenInsideRoot(localFunction, local));
    }

    private static bool IsLocalAssignedByCalledLocalFunctionAfterStartOnSamePath(
        IOperation containingRoot,
        ILocalSymbol local,
        int afterStart,
        IOperation source)
    {
        return containingRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart > afterStart &&
                               ReferenceEquals(invocation.FindOwningExecutableRoot(), containingRoot) &&
                               IsInSameStraightLinePath(invocation, source) &&
                               CanReachDestination(source, invocation) &&
                               TryFindLocalFunction(containingRoot, invocation.TargetMethod, out var localFunction) &&
                               IsLocalWrittenInsideRoot(localFunction, local));
    }

    private static bool TryFindLocalFunction(
        IOperation containingRoot,
        IMethodSymbol targetMethod,
        out ILocalFunctionOperation localFunction)
    {
        foreach (var candidate in containingRoot.Descendants().OfType<ILocalFunctionOperation>())
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.Symbol, targetMethod))
            {
                localFunction = candidate;
                return true;
            }
        }

        localFunction = null!;
        return false;
    }

    private static bool IsLocalWrittenInsideRoot(IOperation containingRoot, ILocalSymbol local)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               IsLocalReference(assignment.Target, local)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local));
    }

    private static bool IsParameterWrittenInsideRoot(IOperation containingRoot, IParameterSymbol parameter)
    {
        return containingRoot.Descendants()
            .OfType<ISimpleAssignmentOperation>()
            .Any(assignment => ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                               IsParameterReference(assignment.Target, parameter)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsParameterReference(argument.Value, parameter));
    }

    private static bool IsParameterReference(IOperation operation, IParameterSymbol parameter)
    {
        return operation.UnwrapConversions() is IParameterReferenceOperation parameterReference &&
               SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter);
    }

    private static bool CanReachDestination(IOperation source, IOperation destination)
    {
        var sourceAncestors = new HashSet<IOperation>();
        for (var current = source.Parent; current != null; current = current.Parent)
        {
            sourceAncestors.Add(current);
        }

        for (var current = destination.Parent; current != null; current = current.Parent)
        {
            if (!sourceAncestors.Contains(current))
                continue;

            var sourceChild = FindDirectChild(current, source);
            var destinationChild = FindDirectChild(current, destination);
            if (ReferenceEquals(sourceChild, destinationChild))
                return true;

            if (current is IConditionalOperation or ISwitchOperation)
                return false;

            return CanFallThroughFromSourceToAncestor(source, current) &&
                   CanFallThroughBetweenChildren(current, sourceChild, destinationChild);
        }

        return true;
    }

    private static bool CanFallThroughFromSourceToAncestor(IOperation source, IOperation ancestor)
    {
        for (var current = source; current.Parent != null && !ReferenceEquals(current.Parent, ancestor); current = current.Parent)
        {
            if (current.Parent is IBlockOperation block &&
                !CanFallThroughAfterOperation(block, current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanReachNextLoopIteration(IOperation source, ILoopOperation loop)
    {
        for (var current = source; current.Parent != null && !ReferenceEquals(current.Parent, loop); current = current.Parent)
        {
            if (current.Parent is IBlockOperation block &&
                !CanBlockReachNextLoopIterationAfterOperation(block, current, loop.Syntax))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanBlockReachNextLoopIterationAfterOperation(
        IBlockOperation block,
        IOperation operation,
        SyntaxNode loopSyntax)
    {
        var child = FindDirectChild(block, operation);
        var operationIndex = IndexOfOperation(block, child);
        if (operationIndex < 0)
            return true;

        for (var index = operationIndex + 1; index < block.Operations.Length; index++)
        {
            if (PreventsNextLoopIteration(block.Operations[index], loopSyntax))
                return false;
        }

        return true;
    }

    private static bool PreventsNextLoopIteration(IOperation operation, SyntaxNode loopSyntax)
    {
        return operation switch
        {
            IBranchOperation { BranchKind: BranchKind.Break } breakOperation =>
                breakOperation.Syntax is BreakStatementSyntax breakStatement &&
                BreakTargetsLoop(breakStatement, loopSyntax),
            IReturnOperation or IThrowOperation => true,
            IBlockOperation block => !CanBlockReachEndOrContinue(block, loopSyntax),
            IConditionalOperation conditional => conditional.WhenFalse != null &&
                                                 PreventsNextLoopIteration(conditional.WhenTrue, loopSyntax) &&
                                                 PreventsNextLoopIteration(conditional.WhenFalse, loopSyntax),
            _ => false,
        };
    }

    private static bool CanBlockReachEndOrContinue(IBlockOperation block, SyntaxNode loopSyntax)
    {
        foreach (var operation in block.Operations)
        {
            if (operation is IBranchOperation { BranchKind: BranchKind.Continue })
                return true;

            if (PreventsNextLoopIteration(operation, loopSyntax))
                return false;
        }

        return true;
    }

    private static bool CanFallThroughBetweenChildren(
        IOperation ancestor,
        IOperation sourceChild,
        IOperation destinationChild)
    {
        if (ancestor is not IBlockOperation block)
            return true;

        var sourceIndex = IndexOfOperation(block, sourceChild);
        var destinationIndex = IndexOfOperation(block, destinationChild);
        if (sourceIndex < 0 || destinationIndex < 0 || sourceIndex >= destinationIndex)
            return true;

        for (var index = sourceIndex + 1; index < destinationIndex; index++)
        {
            if (AlwaysExits(block.Operations[index]))
                return false;
        }

        return true;
    }

    private static bool CanFallThroughAfterOperation(IBlockOperation block, IOperation operation)
    {
        var child = FindDirectChild(block, operation);
        var operationIndex = IndexOfOperation(block, child);
        if (operationIndex < 0)
            return true;

        for (var index = operationIndex + 1; index < block.Operations.Length; index++)
        {
            if (AlwaysExits(block.Operations[index]))
                return false;
        }

        return true;
    }

    private static int IndexOfOperation(IBlockOperation block, IOperation operation)
    {
        for (var index = 0; index < block.Operations.Length; index++)
        {
            if (ReferenceEquals(block.Operations[index], operation))
                return index;
        }

        return -1;
    }

    private static bool AlwaysExits(IOperation operation)
    {
        return operation switch
        {
            IReturnOperation or IThrowOperation => true,
            IBlockOperation block => !CanBlockReachEnd(block),
            IConditionalOperation conditional => conditional.WhenFalse != null &&
                                                 AlwaysExits(conditional.WhenTrue) &&
                                                 AlwaysExits(conditional.WhenFalse),
            _ => false,
        };
    }

    private static bool CanFallThroughFromSourceToAncestorInCurrentIteration(IOperation source, IOperation ancestor)
    {
        for (var current = source; current.Parent != null && !ReferenceEquals(current.Parent, ancestor); current = current.Parent)
        {
            if (current.Parent is IBlockOperation block &&
                !CanFallThroughAfterOperationInCurrentIteration(block, current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanFallThroughAfterOperationInCurrentIteration(IBlockOperation block, IOperation operation)
    {
        var child = FindDirectChild(block, operation);
        var operationIndex = IndexOfOperation(block, child);
        if (operationIndex < 0)
            return true;

        for (var index = operationIndex + 1; index < block.Operations.Length; index++)
        {
            if (AlwaysExitsCurrentIteration(block.Operations[index]))
                return false;
        }

        return true;
    }

    private static bool AlwaysExitsCurrentIteration(IOperation operation)
    {
        return operation switch
        {
            IReturnOperation
                or IThrowOperation
                or IBranchOperation { BranchKind: BranchKind.Break or BranchKind.Continue } => true,
            IBlockOperation block => !CanBlockReachEndCurrentIteration(block),
            IConditionalOperation conditional => conditional.WhenFalse != null &&
                                                 AlwaysExitsCurrentIteration(conditional.WhenTrue) &&
                                                 AlwaysExitsCurrentIteration(conditional.WhenFalse),
            _ => false,
        };
    }

    private static bool CanBlockReachEndCurrentIteration(IBlockOperation block)
    {
        return block.Operations.All(operation => !AlwaysExitsCurrentIteration(operation));
    }

    private static bool CanBlockReachEnd(IBlockOperation block)
    {
        return block.Operations.All(operation => !AlwaysExits(operation));
    }

    private static bool IsInSameStraightLinePath(IOperation assignment, IOperation destination)
    {
        var assignmentAncestors = new HashSet<IOperation>();
        for (var current = assignment.Parent; current != null; current = current.Parent)
        {
            assignmentAncestors.Add(current);
        }

        for (var current = destination.Parent; current != null; current = current.Parent)
        {
            if (!assignmentAncestors.Contains(current))
                continue;

            var assignmentChild = FindDirectChild(current, assignment);
            var destinationChild = FindDirectChild(current, destination);
            return ReferenceEquals(assignmentChild, destinationChild) ||
                   current is not IConditionalOperation and not ISwitchOperation and not ILoopOperation &&
                   assignmentChild is not IConditionalOperation and not ISwitchOperation and not ILoopOperation;
        }

        return true;
    }

    private static IOperation FindDirectChild(IOperation ancestor, IOperation descendant)
    {
        var current = descendant;
        while (current.Parent != null && !ReferenceEquals(current.Parent, ancestor))
        {
            current = current.Parent;
        }

        return current;
    }
}
