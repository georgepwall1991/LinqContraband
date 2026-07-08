using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private static bool IsInsideDelegateCalledFromLoop(IInvocationOperation invocation)
    {
        var anonymousFunction = FindDirectOwningAnonymousFunction(invocation);
        if (anonymousFunction != null)
        {
            return FindContainingExecutableRootForDelegateTarget(anonymousFunction) is { } containingRoot &&
                   TryGetDelegateLocalAssignment(anonymousFunction, out var local, out var assignmentStart) &&
                   IsDelegateLocalCalledFromLoop(containingRoot, local, assignmentStart, invocation);
        }

        var localFunction = FindDirectOwningLocalFunction(invocation);
        return localFunction != null &&
               FindContainingExecutableRoot(localFunction) is { } localRoot &&
               IsLocalFunctionDelegateCalledFromLoop(localRoot, localFunction.Symbol, invocation);
    }

    private static bool IsSaveMethodReferenceAssignedToDelegateCalledFromLoop(IMethodReferenceOperation methodReference)
    {
        return FindContainingExecutableRootForDelegateTarget(methodReference) is { } containingRoot &&
               TryGetDelegateLocalAssignment(methodReference, out var local, out var assignmentStart) &&
               IsDelegateLocalCalledFromLoop(containingRoot, local, assignmentStart, methodReference);
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
            if (current is IMethodBodyBaseOperation or ILocalFunctionOperation)
                return current;

            if (current is IAnonymousFunctionOperation)
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static bool TryGetDelegateLocalAssignment(
        IOperation delegateTarget,
        out ILocalSymbol local,
        out int assignmentStart)
    {
        var current = delegateTarget.Parent;
        while (current != null)
        {
            switch (current)
            {
                case IVariableDeclaratorOperation declarator:
                    local = declarator.Symbol;
                    assignmentStart = declarator.Syntax.SpanStart;
                    return true;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference:
                    local = localReference.Local;
                    assignmentStart = assignment.Syntax.SpanStart;
                    return true;

                case IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation:
                    local = null!;
                    assignmentStart = -1;
                    return false;
            }

            current = current.Parent;
        }

        local = null!;
        assignmentStart = -1;
        return false;
    }

    private static bool IsLocalFunctionDelegateCalledFromLoop(
        IOperation containingRoot,
        IMethodSymbol localFunction,
        IInvocationOperation saveInvocation)
    {
        return FindDelegateLocalAssignments(
                containingRoot,
                operation => IsLocalFunctionReference(operation, localFunction))
            .Any(assignment => IsDelegateLocalCalledFromLoop(
                containingRoot,
                assignment.Local,
                assignment.AssignmentStart,
                saveInvocation));
    }

    private static IEnumerable<(ILocalSymbol Local, int AssignmentStart)> FindDelegateLocalAssignments(
        IOperation containingRoot,
        Func<IOperation, bool> isAssignedTarget)
    {
        foreach (var declarator in containingRoot.Descendants().OfType<IVariableDeclaratorOperation>())
        {
            if (ReferenceEquals(declarator.FindOwningExecutableRoot(), containingRoot) &&
                declarator.Initializer?.Value is { } value &&
                isAssignedTarget(value))
            {
                yield return (declarator.Symbol, declarator.Syntax.SpanStart);
            }
        }

        foreach (var assignment in containingRoot.Descendants().OfType<ISimpleAssignmentOperation>())
        {
            if (ReferenceEquals(assignment.FindOwningExecutableRoot(), containingRoot) &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
                isAssignedTarget(assignment.Value))
            {
                yield return (localReference.Local, assignment.Syntax.SpanStart);
            }
        }
    }

    private static bool IsDelegateLocalCalledFromLoop(
        IOperation containingRoot,
        ILocalSymbol local,
        int assignmentStart,
        IOperation saveOperation)
    {
        foreach (var invocation in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var invocationStart = invocation.Syntax.SpanStart;
            if (!IsDelegateLocalInvocation(invocation, local))
            {
                continue;
            }

            var loop = invocation.FindEnclosingLoop();
            var invocationRoot = invocation.FindOwningExecutableRoot();
            if (invocationRoot is IAnonymousFunctionOperation)
                continue;

            if (loop == null || !invocation.SharesOwningExecutableRoot(loop))
            {
                if (invocationRoot is ILocalFunctionOperation localFunctionWithoutLoop &&
                    IsLocalFunctionCalledFromLoopAfterDelegateAssignment(
                        containingRoot,
                        localFunctionWithoutLoop,
                        invocation,
                        local,
                        assignmentStart,
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            if (IsSaveReceiverFreshContextDeclaredInsideLoopBody(saveOperation, loop, invocation) ||
                IsOperationInsideCatchGuardedRetryAttempt(invocation, loop))
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
                        saveOperation))
                {
                    return true;
                }

                continue;
            }

            if (invocationStart <= assignmentStart)
                continue;

            if (!IsLocalAssignedInStraightLinePathBetween(containingRoot, local, assignmentStart, invocationStart, invocation))
                return true;
        }

        return false;
    }

    private static bool IsDelegateInvocationInsideLocalFunctionCalledAfterAssignment(
        IOperation assignmentRoot,
        IInvocationOperation delegateInvocation,
        ILocalSymbol local,
        int assignmentStart,
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
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, callStart, call) &&
                !IsLocalAssignedBeforeDestinationInRoot(localFunction, local, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, localFunction) &&
                delegateInvocation.Syntax.SpanStart > assignmentStart &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, delegateInvocation.Syntax.SpanStart, delegateInvocation))
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
        IOperation saveOperation)
    {
        var containingRoot = FindContainingExecutableRoot(localFunction);
        if (containingRoot == null)
            return false;

        foreach (var call in containingRoot.Descendants().OfType<IInvocationOperation>())
        {
            var loop = call.FindEnclosingLoop();
            if (loop == null ||
                !call.SharesOwningExecutableRoot(loop) ||
                !ReferenceEquals(call.FindOwningExecutableRoot(), containingRoot) ||
                !SymbolEqualityComparer.Default.Equals(call.TargetMethod, localFunction.Symbol) ||
                IsSaveReceiverFreshContextDeclaredInsideLoopBody(saveOperation, loop, call) ||
                IsOperationInsideCatchGuardedRetryAttempt(call, loop))
            {
                continue;
            }

            var callStart = call.Syntax.SpanStart;
            if (ReferenceEquals(assignmentRoot, containingRoot) &&
                callStart > assignmentStart &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, callStart, call) &&
                !IsLocalAssignedBeforeDestinationInRoot(localFunction, local, delegateInvocation))
            {
                return true;
            }

            if (ReferenceEquals(assignmentRoot, localFunction) &&
                delegateInvocation.Syntax.SpanStart > assignmentStart &&
                !IsLocalAssignedInStraightLinePathBetween(assignmentRoot, local, assignmentStart, delegateInvocation.Syntax.SpanStart, delegateInvocation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLocalFunctionReference(IOperation operation, IMethodSymbol localFunction)
    {
        var value = operation.UnwrapConversions();
        if (value is IDelegateCreationOperation delegateCreation)
            value = delegateCreation.Target.UnwrapConversions();

        return value is IMethodReferenceOperation methodReference &&
               SymbolEqualityComparer.Default.Equals(methodReference.Method, localFunction);
    }

    private static bool IsDelegateLocalInvocation(IInvocationOperation invocation, ILocalSymbol local)
    {
        return invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
               invocation.GetInvocationReceiver() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }

    private static bool IsLocalAssignedInStraightLinePathBetween(
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
                               IsInSameStraightLinePath(assignment, destination)) ||
               containingRoot.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart > afterStart &&
                                    argument.Syntax.SpanStart < beforeStart &&
                                    ReferenceEquals(argument.FindOwningExecutableRoot(), containingRoot) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local) &&
                                    IsInSameStraightLinePath(argument, destination));
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
                                    IsInSameStraightLinePath(argument, destination));
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
