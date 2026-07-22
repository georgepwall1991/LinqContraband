using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed partial class ConcurrentDbContextOperationsAnalyzer
{
    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        foreach (var executableRoot in context.OperationBlocks)
        {
            var directlyReportedInvocations = new HashSet<int>();
            AnalyzeDirectOverlaps(executableRoot, context, directlyReportedInvocations);
            AnalyzeProvablyRepeatedLoopOperations(
                executableRoot,
                context,
                directlyReportedInvocations);
            AnalyzeSelectorFanOut(executableRoot, context);
        }
    }

    private static void AnalyzeDirectOverlaps(
        IOperation executableRoot,
        OperationBlockAnalysisContext context,
        ISet<int> reportedInvocations)
    {
        var operations = EnumerateOutsideNestedExecutables(executableRoot)
            .OfType<IInvocationOperation>()
            .Select(invocation =>
                TryClassifyEfAsyncOperation(
                    invocation,
                    executableRoot,
                    context.CancellationToken,
                    out var operation)
                    ? operation
                    : (EfOperation?)null)
            .Where(operation => operation.HasValue)
            .Select(operation => operation!.Value)
            .OrderBy(operation => operation.Invocation.Syntax.SpanStart)
            .ToArray();

        var reportedOrigins = new HashSet<ContextOrigin>(ContextOriginComparer.Instance);
        for (var currentIndex = 0; currentIndex < operations.Length; currentIndex++)
        {
            var current = operations[currentIndex];
            EfOperation? activePrevious = null;

            for (var previousIndex = currentIndex - 1; previousIndex >= 0; previousIndex--)
            {
                var previous = operations[previousIndex];
                if (!ContextOriginComparer.Instance.Equals(previous.Origin, current.Origin) ||
                    !IsDefinitelyActiveBefore(previous.Invocation, current.Invocation, executableRoot))
                {
                    continue;
                }

                activePrevious = previous;
                break;
            }

            var activeStarts = activePrevious.HasValue
                ? ImmutableArray.Create(activePrevious.Value)
                : ImmutableArray<EfOperation>.Empty;
            if (activeStarts.IsEmpty &&
                !TryGetExhaustiveBranchStarts(
                    operations,
                    currentIndex,
                    current,
                    executableRoot,
                    out activeStarts))
            {
                reportedOrigins.Remove(current.Origin);
                continue;
            }

            if (!reportedOrigins.Add(current.Origin))
                continue;

            reportedInvocations.Add(current.Invocation.Syntax.SpanStart);
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    current.Invocation.Syntax.GetLocation(),
                    activeStarts.Select(start => start.Invocation.Syntax.GetLocation()),
                    properties: null,
                    current.Origin.DisplayName));
        }

    }

    private static bool TryGetExhaustiveBranchStarts(
        IReadOnlyList<EfOperation> operations,
        int currentIndex,
        EfOperation current,
        IOperation executableRoot,
        out ImmutableArray<EfOperation> starts)
    {
        var priorOperations = operations.Take(currentIndex).ToArray();
        foreach (var ifStatement in priorOperations
                     .SelectMany(operation => operation.Invocation.Syntax.Ancestors()
                         .OfType<IfStatementSyntax>())
                     .Distinct()
                     .OrderByDescending(candidate => candidate.SpanStart))
        {
            if (ifStatement.Else == null ||
                ifStatement.Span.Contains(current.Invocation.Syntax.Span) ||
                BranchAlwaysExits(ifStatement.Statement) ||
                BranchAlwaysExits(ifStatement.Else.Statement) ||
                !IsDefinitelyExecutedBefore(
                    ifStatement,
                    current.Invocation.Syntax,
                    executableRoot.Syntax))
            {
                continue;
            }

            var thenStart = FindUnconditionalDiscardedBranchStart(
                priorOperations,
                current.Origin,
                ifStatement.Statement);
            var elseStart = FindUnconditionalDiscardedBranchStart(
                priorOperations,
                current.Origin,
                ifStatement.Else.Statement);
            if ((!thenStart.HasValue || !elseStart.HasValue) &&
                !TryGetExhaustiveAssignedBranchStarts(
                    priorOperations,
                    current,
                    executableRoot,
                    ifStatement.Statement,
                    ifStatement.Else.Statement,
                    out thenStart,
                    out elseStart))
            {
                continue;
            }

            if (!thenStart.HasValue ||
                !elseStart.HasValue ||
                StartCanBeBypassedByTry(thenStart.Value, current.Invocation.Syntax) ||
                StartCanBeBypassedByTry(elseStart.Value, current.Invocation.Syntax) ||
                BranchHasControlTransferAfterStart(
                    ifStatement.Statement,
                    thenStart.Value.Invocation.Syntax) ||
                BranchHasControlTransferAfterStart(
                    ifStatement.Else.Statement,
                    elseStart.Value.Invocation.Syntax))
            {
                continue;
            }

            starts = ImmutableArray.Create(thenStart.Value, elseStart.Value);
            return true;
        }

        starts = ImmutableArray<EfOperation>.Empty;
        return false;
    }

    private static EfOperation? FindUnconditionalDiscardedBranchStart(
        IEnumerable<EfOperation> operations,
        ContextOrigin origin,
        StatementSyntax branch)
    {
        foreach (var operation in operations)
        {
            if (ContextOriginComparer.Instance.Equals(operation.Origin, origin) &&
                branch.Span.Contains(operation.Invocation.Syntax.Span) &&
                IsDirectUnobservedStart(operation.Invocation) &&
                IsUnconditionallyExecutedWithin(operation.Invocation.Syntax, branch))
            {
                return operation;
            }
        }

        return null;
    }

    private static bool TryGetExhaustiveAssignedBranchStarts(
        IReadOnlyList<EfOperation> operations,
        EfOperation current,
        IOperation executableRoot,
        StatementSyntax thenBranch,
        StatementSyntax elseBranch,
        out EfOperation? thenStart,
        out EfOperation? elseStart)
    {
        if (!TryFindUnconditionalAssignedBranchStart(
                operations,
                current.Origin,
                thenBranch,
                out var resolvedThenStart,
                out var thenAssignment,
                out var thenLocal) ||
            !TryFindUnconditionalAssignedBranchStart(
                operations,
                current.Origin,
                elseBranch,
                out var resolvedElseStart,
                out var elseAssignment,
                out var elseLocal))
        {
            thenStart = null;
            elseStart = null;
            return false;
        }

        var sameLocal = SymbolEqualityComparer.Default.Equals(thenLocal, elseLocal);
        var thenAssignments = sameLocal
            ? new[] { thenAssignment, elseAssignment }
            : new[] { thenAssignment };
        var elseAssignments = sameLocal
            ? thenAssignments
            : new[] { elseAssignment };
        if (!AssignedBranchLocalRemainsActive(
                executableRoot,
                thenLocal,
                thenAssignments,
                current.Invocation.Syntax.SpanStart) ||
            !AssignedBranchLocalRemainsActive(
                executableRoot,
                elseLocal,
                elseAssignments,
                current.Invocation.Syntax.SpanStart))
        {
            thenStart = null;
            elseStart = null;
            return false;
        }

        thenStart = resolvedThenStart;
        elseStart = resolvedElseStart;
        return true;
    }

    private static bool AssignedBranchLocalRemainsActive(
        IOperation executableRoot,
        ILocalSymbol local,
        IReadOnlyList<ISimpleAssignmentOperation> branchAssignments,
        int beforePosition)
    {
        var assignments = LocalAssignmentCache.GetAssignments(executableRoot, local)
            .Where(assignment => assignment.SpanStart < beforePosition)
            .ToArray();
        if (assignments.Length != branchAssignments.Count ||
            branchAssignments.Any(branchAssignment => !assignments.Any(assignment =>
                assignment.SpanStart == branchAssignment.Syntax.SpanStart)) ||
            !LocalHasNoUntrackedWritesBefore(executableRoot, local, beforePosition) ||
            EnumerateOutsideNestedExecutables(executableRoot).Any(operation =>
                operation.Syntax.SpanStart < beforePosition &&
                operation is IDynamicInvocationOperation dynamicInvocation &&
                dynamicInvocation.ReferencesLocal(local)))
        {
            return false;
        }

        var earliestAssignment = branchAssignments.Min(assignment =>
            assignment.Syntax.SpanStart);
        return !executableRoot.Descendants().OfType<ILocalReferenceOperation>().Any(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
            !branchAssignments.Any(assignment =>
                assignment.Target.Syntax.Span.Contains(reference.Syntax.Span)) &&
            CanOperationOrNestedExecutableRunBetween(
                reference,
                executableRoot,
                earliestAssignment,
                beforePosition));
    }

    private static bool TryFindUnconditionalAssignedBranchStart(
        IEnumerable<EfOperation> operations,
        ContextOrigin origin,
        StatementSyntax branch,
        out EfOperation start,
        out ISimpleAssignmentOperation assignment,
        out ILocalSymbol local)
    {
        foreach (var operation in operations)
        {
            if (!ContextOriginComparer.Instance.Equals(operation.Origin, origin) ||
                !branch.Span.Contains(operation.Invocation.Syntax.Span) ||
                !IsUnconditionallyExecutedWithin(operation.Invocation.Syntax, branch) ||
                !TryGetDirectAssignedTaskLocal(
                    operation.Invocation,
                    out assignment,
                    out local))
            {
                continue;
            }

            start = operation;
            return true;
        }

        start = default;
        assignment = null!;
        local = null!;
        return false;
    }

    private static bool TryGetDirectAssignedTaskLocal(
        IInvocationOperation invocation,
        out ISimpleAssignmentOperation assignment,
        out ILocalSymbol local)
    {
        IOperation current = invocation;
        while (current.Parent != null)
        {
            if (current.Parent is IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IConversionOperation conversion &&
                conversion.OperatorMethod == null)
            {
                current = conversion;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            break;
        }

        if (current.Parent is ISimpleAssignmentOperation simpleAssignment &&
            ReferenceEquals(simpleAssignment.Value, current) &&
            simpleAssignment.Target.UnwrapConversions() is ILocalReferenceOperation target &&
            IsStandaloneAssignment(simpleAssignment))
        {
            assignment = simpleAssignment;
            local = target.Local;
            return true;
        }

        assignment = null!;
        local = null!;
        return false;
    }

    private static bool IsStandaloneAssignment(ISimpleAssignmentOperation assignment)
    {
        IOperation current = assignment;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        return current.Parent is IExpressionStatementOperation;
    }

    private static bool StartCanBeBypassedByTry(
        EfOperation start,
        SyntaxNode current)
    {
        return start.Invocation.Syntax.Ancestors().OfType<TryStatementSyntax>().Any(tryStatement =>
            tryStatement.Block.Span.Contains(start.Invocation.Syntax.Span) &&
            !tryStatement.Block.Span.Contains(current.Span) &&
            (tryStatement.Catches.Count > 0 ||
             tryStatement.Finally?.Span.Contains(current.Span) == true));
    }

    private static bool BranchHasControlTransferAfterStart(
        StatementSyntax branch,
        SyntaxNode start)
    {
        return branch.DescendantNodes().Any(candidate =>
            candidate.SpanStart > start.SpanStart &&
            !IsInsideNestedExecutableSyntax(candidate, branch) &&
            candidate is ReturnStatementSyntax or
                ThrowStatementSyntax or
                ThrowExpressionSyntax or
                GotoStatementSyntax or
                YieldStatementSyntax or
                BreakStatementSyntax or
                ContinueStatementSyntax);
    }

    private static bool IsDirectUnobservedStart(IInvocationOperation invocation)
    {
        IOperation current = invocation;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        return current.Parent is IExpressionStatementOperation ||
               current.Parent is ISimpleAssignmentOperation assignment &&
               assignment.Target.UnwrapConversions() is IDiscardOperation;
    }

    private static bool IsDefinitelyActiveBefore(
        IInvocationOperation previous,
        IInvocationOperation current,
        IOperation executableRoot)
    {
        if (previous.Syntax.SpanStart >= current.Syntax.SpanStart ||
            !IsDefinitelyExecutedBefore(previous.Syntax, current.Syntax, executableRoot.Syntax))
        {
            return false;
        }

        if (!TaskStartCanReachCurrentHandler(previous, current, executableRoot))
            return false;

        if (TryGetImmediateAwait(previous, out var immediateAwait) &&
            !CompletionCanBeBypassedByContinuingHandler(
                previous.Syntax,
                immediateAwait.Syntax,
                immediateAwait.Operation.Syntax.Span.End,
                current.Syntax,
                executableRoot))
        {
            return false;
        }

        if (TryGetContainingAwaitedWhenAll(
                previous,
                out var completedWhenAll,
                out var whenAllAwait) &&
            completedWhenAll.Syntax.Span.End <= current.Syntax.SpanStart &&
            !CompletionCanBeBypassedByContinuingHandler(
                previous.Syntax,
                whenAllAwait.Syntax,
                whenAllAwait.Operation.Syntax.Span.End,
                current.Syntax,
                executableRoot))
        {
            return false;
        }

        if (TryGetContainingAwaitedSingleTaskWhenAny(
                previous,
                out var completedWhenAny,
                out var whenAnyAwait) &&
            completedWhenAny.Syntax.Span.End <= current.Syntax.SpanStart &&
            !CompletionCanBeBypassedByContinuingHandler(
                previous.Syntax,
                whenAnyAwait.Syntax,
                whenAnyAwait.Operation.Syntax.Span.End,
                current.Syntax,
                executableRoot))
        {
            return false;
        }

        if (EscapesDirectly(previous, current))
            return false;

        if (!TryGetAssignedTaskLocal(previous, out var taskLocal))
            return true;

        var assignmentsBeforeCurrent = LocalAssignmentCache.GetAssignments(executableRoot, taskLocal)
            .Count(assignment => assignment.SpanStart < current.Syntax.SpanStart);
        if (assignmentsBeforeCurrent != 1)
            return false;

        var taskEndReferences = new List<ILocalReferenceOperation>();
        foreach (var operation in executableRoot.Descendants())
        {
            if (operation is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, taskLocal) ||
                localReference.Syntax.SpanStart <= previous.Syntax.SpanStart ||
                localReference.Syntax.SpanStart >= current.Syntax.SpanStart)
            {
                continue;
            }

            if (!IsReferenceAwaited(localReference) &&
                !IsReferenceInAwaitedWhenAll(localReference, executableRoot) &&
                !IsReferenceInAwaitedSingleTaskWhenAny(localReference) &&
                !TryGetSynchronousTaskCompletion(localReference, out _) &&
                !EscapesToUnknownConsumer(localReference))
            {
                continue;
            }

            taskEndReferences.Add(localReference);
            if (IsDefinitelyExecutedBefore(
                    localReference.Syntax,
                    current.Syntax,
                    executableRoot.Syntax) &&
                !TaskEndCanBeBypassedByContinuingHandler(
                    previous.Syntax,
                    localReference,
                    current.Syntax,
                    executableRoot))
            {
                return false;
            }
        }

        return !TaskEndsOnEveryBranch(
            taskEndReferences,
            previous.Syntax,
            current.Syntax,
            executableRoot);
    }

    private static bool TaskEndsOnEveryBranch(
        IReadOnlyCollection<ILocalReferenceOperation> taskEndReferences,
        SyntaxNode taskStart,
        SyntaxNode current,
        IOperation executableRoot)
    {
        var completionPoints = taskEndReferences
            .SelectMany(reference => GetTaskCompletionPoints(reference, executableRoot))
            .ToImmutableArray();
        return TaskCompletionPointsEndOnEveryBranch(
            completionPoints,
            taskStart,
            current,
            executableRoot);
    }

    private static bool TaskEndCanBeBypassedByContinuingHandler(
        SyntaxNode taskStart,
        ILocalReferenceOperation taskEnd,
        SyntaxNode current,
        IOperation executableRoot)
    {
        if (TryGetSynchronousTaskCompletion(taskEnd, out var synchronousCompletion))
        {
            return CompletionCanBeBypassedByContinuingHandler(
                taskStart,
                synchronousCompletion.Syntax,
                synchronousCompletion.Syntax.SpanStart,
                current,
                executableRoot);
        }

        if (TryGetImmediateAwait(taskEnd, out var immediateAwait))
        {
            return CompletionCanBeBypassedByContinuingHandler(
                taskStart,
                immediateAwait.Syntax,
                immediateAwait.Operation.Syntax.Span.End,
                current,
                executableRoot);
        }

        if (TryGetContainingAwaitedWhenAll(taskEnd, out _, out var whenAllAwait))
        {
            return CompletionCanBeBypassedByContinuingHandler(
                taskStart,
                whenAllAwait.Syntax,
                whenAllAwait.Operation.Syntax.Span.End,
                current,
                executableRoot);
        }

        if (taskEnd is ILocalReferenceOperation arrayElementReference)
        {
            var arrayWhenAllAwaits = GetAwaitedWhenAllForStableArrayElement(
                arrayElementReference,
                executableRoot);
            if (!arrayWhenAllAwaits.IsDefaultOrEmpty)
            {
                foreach (var arrayWhenAllAwait in arrayWhenAllAwaits)
                {
                    if (arrayWhenAllAwait.Syntax.SpanStart >= current.SpanStart ||
                        !IsDefinitelyExecutedBefore(
                            arrayWhenAllAwait.Syntax,
                            current,
                            executableRoot.Syntax) ||
                        CompletionCanBeBypassedByContinuingHandler(
                            taskStart,
                            arrayWhenAllAwait.Syntax,
                            arrayWhenAllAwait.Operation.Syntax.Span.End,
                            current,
                            executableRoot))
                    {
                        continue;
                    }

                    return false;
                }

                var completionPoints = arrayWhenAllAwaits
                    .Select(completion => (
                        Completion: (SyntaxNode)completion.Syntax,
                        ThrowingPrefixEnd: completion.Operation.Syntax.Span.End))
                    .ToImmutableArray();
                return !TaskCompletionPointsEndOnEveryBranch(
                    completionPoints,
                    taskStart,
                    current,
                    executableRoot);
            }
        }

        if (TryGetContainingAwaitedSingleTaskWhenAny(
                taskEnd,
                out var anonymousFunction,
                out var whenAnyAwait))
        {
            return CompletionCanBeBypassedByContinuingHandler(
                taskStart,
                whenAnyAwait.Syntax,
                whenAnyAwait.Operation.Syntax.Span.End,
                current,
                executableRoot);
        }

        return CompletionCanBeBypassedByContinuingHandler(
            taskStart,
            taskEnd.Syntax,
            taskEnd.Syntax.SpanStart,
            current,
            executableRoot);
    }

    private static ImmutableArray<(SyntaxNode Completion, int ThrowingPrefixEnd)>
        GetTaskCompletionPoints(
            ILocalReferenceOperation taskEnd,
            IOperation executableRoot)
    {
        if (TryGetSynchronousTaskCompletion(taskEnd, out var synchronousCompletion))
        {
            return ImmutableArray.Create((
                Completion: (SyntaxNode)synchronousCompletion.Syntax,
                ThrowingPrefixEnd: synchronousCompletion.Syntax.SpanStart));
        }

        if (TryGetImmediateAwait(taskEnd, out var immediateAwait))
        {
            return ImmutableArray.Create((
                Completion: (SyntaxNode)immediateAwait.Syntax,
                ThrowingPrefixEnd: immediateAwait.Operation.Syntax.Span.End));
        }

        if (TryGetContainingAwaitedWhenAll(taskEnd, out _, out var whenAllAwait))
        {
            return ImmutableArray.Create((
                Completion: (SyntaxNode)whenAllAwait.Syntax,
                ThrowingPrefixEnd: whenAllAwait.Operation.Syntax.Span.End));
        }

        var arrayWhenAllAwaits = GetAwaitedWhenAllForStableArrayElement(
            taskEnd,
            executableRoot);
        if (!arrayWhenAllAwaits.IsDefaultOrEmpty)
        {
            return arrayWhenAllAwaits
                .Select(completion => (
                    Completion: (SyntaxNode)completion.Syntax,
                    ThrowingPrefixEnd: completion.Operation.Syntax.Span.End))
                .ToImmutableArray();
        }

        if (TryGetContainingAwaitedSingleTaskWhenAny(
                taskEnd,
                out _,
                out var whenAnyAwait))
        {
            return ImmutableArray.Create((
                Completion: (SyntaxNode)whenAnyAwait.Syntax,
                ThrowingPrefixEnd: whenAnyAwait.Operation.Syntax.Span.End));
        }

        return ImmutableArray.Create((
            Completion: (SyntaxNode)taskEnd.Syntax,
            ThrowingPrefixEnd: taskEnd.Syntax.SpanStart));
    }

    private static bool CompletionCanBeBypassedByContinuingHandler(
        SyntaxNode taskStart,
        SyntaxNode completion,
        int throwingPrefixEnd,
        SyntaxNode current,
        IOperation executableRoot)
    {
        foreach (var tryStatement in completion.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(completion.Span) ||
                tryStatement.Block.Span.Contains(current.Span))
            {
                continue;
            }

            var currentCatch = tryStatement.Catches.FirstOrDefault(catchClause =>
                catchClause.Span.Contains(current.Span));
            var currentIsInFinally = tryStatement.Finally?.Span.Contains(current.Span) == true;
            var handlerCanReachFollowingOperation =
                tryStatement.Span.End <= current.SpanStart &&
                tryStatement.Catches.Any(catchClause => !BranchAlwaysExits(catchClause.Block));
            if (currentCatch == null &&
                !currentIsInFinally &&
                !handlerCanReachFollowingOperation)
            {
                continue;
            }

            var eligibleCatches = currentCatch != null
                ? new[] { currentCatch }
                : tryStatement.Catches
                    .Where(catchClause => !BranchAlwaysExits(catchClause.Block));

            if (EnumerateOutsideNestedExecutables(executableRoot)
                .OfType<IInvocationOperation>()
                .Any(invocation =>
                    tryStatement.Block.Span.Contains(invocation.Syntax.Span) &&
                    completion.Span.Contains(invocation.Syntax.Span) &&
                    IsTaskWhenAll(invocation) &&
                    !TaskCombinatorInputsAreDefinitelyNonNull(invocation, executableRoot) &&
                    (currentIsInFinally || eligibleCatches.Any(catchClause =>
                        OperationCanReachCatch(
                            invocation,
                            catchClause,
                            tryStatement,
                            executableRoot)))))
            {
                return true;
            }

            if (EnumerateOutsideNestedExecutables(executableRoot).Any(operation =>
                    operation.Syntax.SpanStart >= taskStart.Span.End &&
                    operation.Syntax.Span.End <= throwingPrefixEnd &&
                    !operation.Syntax.Span.Equals(completion.Span) &&
                    tryStatement.Block.Span.Contains(operation.Syntax.Span) &&
                    CanThrowBeforeTaskEnd(operation, executableRoot) &&
                    (currentIsInFinally || eligibleCatches.Any(catchClause =>
                        OperationCanReachCatch(
                            operation,
                            catchClause,
                            tryStatement,
                            executableRoot)))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanThrowBeforeTaskEnd(
        IOperation operation,
        IOperation executableRoot)
    {
        if (operation is IInvocationOperation invocation)
        {
            if (IsTaskWhenAll(invocation))
                return !TaskCombinatorInputsAreDefinitelyNonNull(invocation, executableRoot);

            if (IsTaskWhenAny(invocation))
            {
                return !TaskWhenAnyHasExactlyOneInput(invocation) ||
                       !TaskCombinatorInputsAreDefinitelyNonNull(invocation, executableRoot);
            }

            if (IsTaskCompletionWrapper(invocation))
                return false;

            return true;
        }

        if (operation is IPropertyReferenceOperation propertyReference &&
            IsTaskCompletedTaskProperty(propertyReference))
        {
            return false;
        }

        return operation is IObjectCreationOperation or
            IThrowOperation or
            IAwaitOperation or
            IPropertyReferenceOperation or
            IArrayElementReferenceOperation;
    }

    private static bool TaskStartCanReachCurrentHandler(
        IInvocationOperation taskStart,
        IInvocationOperation current,
        IOperation executableRoot)
    {
        foreach (var tryStatement in taskStart.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(taskStart.Syntax.Span))
            {
                continue;
            }

            var currentCatch = tryStatement.Catches.FirstOrDefault(catchClause =>
                catchClause.Span.Contains(current.Syntax.Span));
            if (currentCatch == null)
                continue;

            return EnumerateOutsideNestedExecutables(executableRoot).Any(operation =>
                operation.Syntax.SpanStart >= taskStart.Syntax.Span.End &&
                operation.Syntax.Span.End <= tryStatement.Block.Span.End &&
                IsDefinitelyExecutedBefore(
                    taskStart.Syntax,
                    operation.Syntax,
                    executableRoot.Syntax) &&
                CanThrowBeforeTaskEnd(operation, executableRoot) &&
                OperationCanReachCatch(
                    operation,
                    currentCatch,
                    tryStatement,
                    executableRoot));
        }

        return true;
    }

    private static bool OperationCanReachCatch(
        IOperation operation,
        CatchClauseSyntax targetCatch,
        TryStatementSyntax targetTry,
        IOperation executableRoot,
        ITypeSymbol? knownExceptionType = null,
        bool knownExceptionTypeIsExact = false)
    {
        var semanticModel = executableRoot.SemanticModel;
        if (GetCatchFilterConstantValue(targetCatch, semanticModel) == false)
            return false;

        if (operation is IObjectCreationOperation &&
            IsDirectExceptionConstruction(operation))
        {
            return false;
        }

        var inputFaultCombinator = operation switch
        {
            IInvocationOperation invocation => invocation,
            IAwaitOperation awaitOperation => GetAwaitedInputFaultCombinator(awaitOperation),
            _ => null
        };
        if (knownExceptionType == null &&
            inputFaultCombinator != null &&
            (IsTaskWhenAll(inputFaultCombinator) || IsTaskWhenAny(inputFaultCombinator)) &&
            !TaskCombinatorInputsAreDefinitelyNonNull(inputFaultCombinator, executableRoot) &&
            TryGetTaskCombinatorInputException(
                inputFaultCombinator,
                executableRoot,
                out var inferredExceptionType,
                out var inferredExceptionTypeIsExact))
        {
            knownExceptionType = inferredExceptionType;
            knownExceptionTypeIsExact = inferredExceptionTypeIsExact;
        }

        if (knownExceptionType != null &&
            !(knownExceptionTypeIsExact
                ? CatchDefinitelyHandlesException(
                    targetCatch,
                    knownExceptionType,
                    semanticModel)
                : CatchMayHandleException(
                    targetCatch,
                    knownExceptionType,
                    semanticModel)))
        {
            return false;
        }

        if (operation is not IThrowOperation throwOperation)
            return true;

        var thrownType = GetThrownExceptionType(
            throwOperation,
            executableRoot,
            out var thrownTypeIsExact);
        var catchCanHandleThrownType = thrownTypeIsExact
            ? CatchDefinitelyHandlesException(targetCatch, thrownType, semanticModel)
            : CatchMayHandleException(targetCatch, thrownType, semanticModel);
        return catchCanHandleThrownType &&
               !ThrowIsDefinitelyIntercepted(
                   throwOperation,
                   thrownType,
                   targetTry,
                   semanticModel);
    }

    private static ITypeSymbol? GetThrownExceptionType(
        IThrowOperation throwOperation,
        IOperation executableRoot,
        out bool isExact)
    {
        var exception = throwOperation.Exception?.UnwrapConversions();
        if (exception is ILocalReferenceOperation localReference &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                throwOperation.Syntax.SpanStart,
                out var assignedValue,
                default) &&
            assignedValue.UnwrapConversions() is IObjectCreationOperation objectCreation &&
            objectCreation.Type != null)
        {
            isExact = LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                throwOperation.Syntax.SpanStart);
            return objectCreation.Type;
        }

        isExact = exception is IObjectCreationOperation;
        return exception?.Type;
    }

    private static bool LocalHasNoUntrackedWritesBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition)
    {
        foreach (var operation in executableRoot.Descendants())
        {
            if (!CanOperationRunBefore(operation, executableRoot, beforePosition))
                continue;

            if (operation is IAssignmentOperation assignment &&
                assignment.Target.ReferencesLocal(local) &&
                (operation is not ISimpleAssignmentOperation ||
                 IsInsideNestedExecutable(operation, executableRoot)))
            {
                return false;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Symbol.RefKind != RefKind.None &&
                declarator.Initializer?.Value.ReferencesLocal(local) == true)
            {
                return false;
            }

            if (operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesLocal(local))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetTaskCombinatorInputException(
        IInvocationOperation combinator,
        IOperation executableRoot,
        out ITypeSymbol exceptionType,
        out bool exceptionTypeIsExact)
    {
        var inputs = combinator.Arguments
            .Select(argument => ResolveStableTaskCombinatorInput(
                argument.Value,
                executableRoot,
                combinator.Syntax.SpanStart))
            .ToArray();
        var hasNullCollection = inputs.Any(IsDefinitelyNullTaskInput);
        var hasNullElement = inputs.Any(input =>
            input is IArrayCreationOperation
            {
                Initializer: { } initializer
            } && initializer.ElementValues.Any(IsDefinitelyNullTaskInput));

        exceptionType = executableRoot.SemanticModel?.Compilation.GetTypeByMetadataName(
            hasNullCollection
                ? "System.ArgumentNullException"
                : "System.ArgumentException")!;
        exceptionTypeIsExact = hasNullCollection || hasNullElement;

        return exceptionType != null;
    }

    private static IInvocationOperation? GetAwaitedInputFaultCombinator(
        IAwaitOperation awaitOperation)
    {
        var current = awaitOperation.Operation.UnwrapConversions();
        while (current is IInvocationOperation wrapper && IsTaskCompletionWrapper(wrapper))
        {
            var receiver = wrapper.GetInvocationReceiver(false);
            if (receiver == null)
                return null;

            current = receiver.UnwrapConversions();
        }

        return current as IInvocationOperation;
    }

    private static IOperation ResolveStableTaskCombinatorInput(
        IOperation value,
        IOperation executableRoot,
        int beforePosition)
    {
        value = value.UnwrapConversions();
        if (value is ILocalReferenceOperation localReference)
        {
            if (LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                    executableRoot,
                    localReference.Local,
                    beforePosition,
                    out var assignedValue,
                    default))
            {
                return assignedValue.UnwrapConversions();
            }
        }

        return value;
    }

    private static bool IsDefinitelyNullTaskInput(IOperation value)
    {
        value = value.UnwrapConversions();
        return value.ConstantValue is { HasValue: true, Value: null };
    }

    private static bool IsDirectExceptionConstruction(IOperation operation)
    {
        IOperation current = operation;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        return current.Parent is IThrowOperation;
    }

    private static bool ThrowIsDefinitelyIntercepted(
        IThrowOperation throwOperation,
        ITypeSymbol? thrownType,
        TryStatementSyntax targetTry,
        SemanticModel? semanticModel)
    {
        foreach (var nestedTry in throwOperation.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (ReferenceEquals(nestedTry, targetTry))
                break;

            if (!nestedTry.Block.Span.Contains(throwOperation.Syntax.Span))
                continue;

            foreach (var catchClause in nestedTry.Catches)
            {
                if (!CatchDefinitelyHandlesException(catchClause, thrownType, semanticModel))
                    continue;

                var filterValue = GetCatchFilterConstantValue(catchClause, semanticModel);
                if (filterValue == true)
                    return true;
            }
        }

        return false;
    }

    private static bool CatchMayHandleException(
        CatchClauseSyntax catchClause,
        ITypeSymbol? thrownType,
        SemanticModel? semanticModel)
    {
        if (catchClause.Declaration?.Type == null ||
            thrownType == null ||
            semanticModel == null)
        {
            return true;
        }

        var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        if (catchType == null || catchType.TypeKind == TypeKind.Error)
            return true;

        return TypeIsSameOrDerivedFrom(thrownType, catchType) ||
               TypeIsSameOrDerivedFrom(catchType, thrownType);
    }

    private static bool CatchDefinitelyHandlesException(
        CatchClauseSyntax catchClause,
        ITypeSymbol? thrownType,
        SemanticModel? semanticModel)
    {
        if (catchClause.Declaration?.Type == null)
            return true;

        if (thrownType == null || semanticModel == null)
            return false;

        var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        return catchType != null &&
               catchType.TypeKind != TypeKind.Error &&
               TypeIsSameOrDerivedFrom(thrownType, catchType);
    }

    private static bool TypeIsSameOrDerivedFrom(
        ITypeSymbol type,
        ITypeSymbol possibleBaseType)
    {
        for (var current = type as INamedTypeSymbol;
             current != null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, possibleBaseType))
                return true;
        }

        return false;
    }

    private static bool? GetCatchFilterConstantValue(
        CatchClauseSyntax catchClause,
        SemanticModel? semanticModel)
    {
        if (catchClause.Filter == null)
            return true;

        var constant = semanticModel?.GetConstantValue(catchClause.Filter.FilterExpression);
        return constant is { HasValue: true, Value: bool value } ? value : null;
    }

    private static bool IsUnconditionallyExecutedWithin(
        SyntaxNode operation,
        SyntaxNode branch)
    {
        if (HasPotentialControlTransferBefore(operation, branch))
            return false;

        for (SyntaxNode? current = operation; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, branch) && current is SwitchSectionSyntax)
                return true;

            if (current is BinaryExpressionSyntax binary &&
                (binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression) ||
                 binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression) ||
                 binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression)) &&
                binary.Right.Span.Contains(operation.Span))
            {
                return false;
            }

            if (current is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceAssignmentExpression) &&
                assignment.Right.Span.Contains(operation.Span))
            {
                return false;
            }

            if (current is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(operation.Span))
            {
                return false;
            }

            if (current is IfStatementSyntax ifStatement)
            {
                if (!ifStatement.Condition.Span.Contains(operation.Span))
                    return false;

                continue;
            }

            if (current is ConditionalExpressionSyntax conditionalExpression)
            {
                if (!conditionalExpression.Condition.Span.Contains(operation.Span))
                    return false;

                continue;
            }

            if (current is WhileStatementSyntax whileStatement)
            {
                if (!whileStatement.Condition.Span.Contains(operation.Span))
                    return false;

                continue;
            }

            if (current is ForStatementSyntax forStatement)
            {
                var isInitialExecution =
                    forStatement.Declaration?.Span.Contains(operation.Span) == true ||
                    forStatement.Initializers.Any(initializer => initializer.Span.Contains(operation.Span)) ||
                    forStatement.Condition?.Span.Contains(operation.Span) == true;
                if (!isInitialExecution)
                    return false;

                continue;
            }

            if (current is ForEachStatementSyntax forEachStatement)
            {
                if (!forEachStatement.Expression.Span.Contains(operation.Span))
                    return false;

                continue;
            }

            if (current is ForEachVariableStatementSyntax forEachVariableStatement)
            {
                if (!forEachVariableStatement.Expression.Span.Contains(operation.Span))
                    return false;

                continue;
            }

            if (current is SwitchSectionSyntax or
                SwitchExpressionArmSyntax or
                CatchClauseSyntax or
                AnonymousFunctionExpressionSyntax or
                LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (ReferenceEquals(current, branch))
                return true;
        }

        return false;
    }

    private static bool HasPotentialControlTransferBefore(
        SyntaxNode operation,
        SyntaxNode branch)
    {
        foreach (var candidate in branch.DescendantNodes())
        {
            if (candidate.SpanStart >= operation.SpanStart ||
                candidate.Span.Contains(operation.Span) ||
                IsInsideNestedExecutableSyntax(candidate, branch))
            {
                continue;
            }

            switch (candidate)
            {
                case ReturnStatementSyntax:
                case ThrowStatementSyntax:
                case ThrowExpressionSyntax:
                case GotoStatementSyntax:
                case YieldStatementSyntax yieldStatement
                    when yieldStatement.IsKind(
                        Microsoft.CodeAnalysis.CSharp.SyntaxKind.YieldBreakStatement):
                    return true;

                case BreakStatementSyntax breakStatement:
                    var breakTarget = breakStatement.Ancestors().FirstOrDefault(ancestor =>
                        ancestor is WhileStatementSyntax or
                            DoStatementSyntax or
                            ForStatementSyntax or
                            ForEachStatementSyntax or
                            ForEachVariableStatementSyntax or
                            SwitchStatementSyntax);
                    if (breakTarget?.Span.Contains(operation.Span) == true)
                        return true;

                    break;

                case ContinueStatementSyntax continueStatement:
                    var continueTarget = continueStatement.Ancestors().FirstOrDefault(ancestor =>
                        ancestor is WhileStatementSyntax or
                            DoStatementSyntax or
                            ForStatementSyntax or
                            ForEachStatementSyntax or
                            ForEachVariableStatementSyntax);
                    if (continueTarget?.Span.Contains(operation.Span) == true)
                        return true;

                    break;
            }
        }

        return false;
    }

    private static bool IsInsideNestedExecutableSyntax(
        SyntaxNode node,
        SyntaxNode branch)
    {
        for (var current = node.Parent;
             current != null && !ReferenceEquals(current, branch);
             current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return true;
        }

        return false;
    }

    private static bool EscapesDirectly(
        IInvocationOperation invocation,
        IInvocationOperation laterInvocation)
    {
        IOperation current = invocation;
        while (current.Parent != null)
        {
            if (current.Parent is IConversionOperation or IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            if (current.Parent is IArgumentOperation argument)
            {
                if (argument.Parent is IInvocationOperation consumer)
                {
                    return !consumer.Syntax.Span.Contains(laterInvocation.Syntax.Span) &&
                           !IsTaskWhenAll(consumer) &&
                           !IsTaskWhenAny(consumer);
                }

                if (argument.Parent is IObjectCreationOperation objectCreation)
                {
                    return !objectCreation.Syntax.Span.Contains(laterInvocation.Syntax.Span);
                }
            }

            if (current.Parent is IReturnOperation)
                return true;

            if (current.Parent is ISimpleAssignmentOperation assignment)
            {
                var target = assignment.Target.UnwrapConversions();
                return target is not ILocalReferenceOperation and not IDiscardOperation;
            }

            return false;
        }

        return false;
    }

    private static bool IsImmediatelyAwaited(IInvocationOperation invocation)
    {
        return TryGetImmediateAwait(invocation, out _);
    }

    private static bool TryGetImmediateAwait(
        IOperation operation,
        out IAwaitOperation awaitOperation)
    {
        IOperation current = operation;
        while (current.Parent != null)
        {
            if (current.Parent is IAwaitOperation parentAwait)
            {
                awaitOperation = parentAwait;
                return true;
            }

            if (current.Parent is IConversionOperation or IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            if (current.Parent is IArgumentOperation argument &&
                argument.Parent is IInvocationOperation argumentWrapper &&
                IsTaskWrapper(argumentWrapper, current))
            {
                current = argumentWrapper;
                continue;
            }

            awaitOperation = null!;
            return false;
        }

        awaitOperation = null!;
        return false;
    }

    private static bool TryGetAssignedTaskLocal(
        IInvocationOperation invocation,
        out ILocalSymbol local)
    {
        IOperation current = invocation;
        for (var ancestor = invocation.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is IInvocationOperation containingCombinator &&
                (IsTaskWhenAll(containingCombinator) ||
                 IsTaskWhenAny(containingCombinator) &&
                 TaskWhenAnyHasExactlyOneInput(containingCombinator)))
            {
                current = containingCombinator;
            }

            if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        while (current.Parent != null)
        {
            if (current.Parent is IConversionOperation or IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            if (current.Parent is IVariableInitializerOperation initializer &&
                initializer.Parent is IVariableDeclaratorOperation declarator)
            {
                local = declarator.Symbol;
                return true;
            }

            if (current.Parent is ISimpleAssignmentOperation assignment &&
                ReferenceEquals(assignment.Value, current) &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation target)
            {
                local = target.Local;
                return true;
            }

            break;
        }

        local = null!;
        return false;
    }

    private static bool TryGetSynchronousTaskCompletion(
        IOperation reference,
        out IInvocationOperation completion)
    {
        IOperation current = reference;
        while (current.Parent != null)
        {
            if (current.Parent is IConversionOperation or IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskCompletionWrapper(wrapper) &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            break;
        }

        if (current.Parent is not IInvocationOperation taskInvocation ||
            !ReferenceEquals(taskInvocation.GetInvocationReceiver(false), current))
        {
            completion = null!;
            return false;
        }

        if (IsTaskInstanceMethod(taskInvocation, "Wait") &&
            taskInvocation.TargetMethod.Parameters.Length == 0)
        {
            completion = taskInvocation;
            return true;
        }

        if (!IsTaskInstanceMethod(taskInvocation, "GetAwaiter") ||
            taskInvocation.TargetMethod.Parameters.Length != 0)
        {
            completion = null!;
            return false;
        }

        current = taskInvocation;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        if (current.Parent is IInvocationOperation getResult &&
            ReferenceEquals(getResult.GetInvocationReceiver(false), current) &&
            getResult.TargetMethod.Name == "GetResult" &&
            getResult.TargetMethod.Parameters.Length == 0)
        {
            completion = getResult;
            return true;
        }

        completion = null!;
        return false;
    }

    private static bool IsTaskInstanceMethod(
        IInvocationOperation invocation,
        string methodName)
    {
        var method = invocation.TargetMethod;
        return method.Name == methodName &&
               method.ContainingType.Name is "Task" or "ValueTask" &&
               method.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool IsReferenceAwaited(ILocalReferenceOperation reference)
    {
        IOperation current = reference;
        while (current.Parent != null)
        {
            if (current.Parent is IAwaitOperation)
                return true;

            if (current.Parent is IConversionOperation or IParenthesizedOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is IInvocationOperation wrapper &&
                IsTaskWrapper(wrapper, current))
            {
                current = wrapper;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsReferenceInAwaitedWhenAll(
        ILocalReferenceOperation reference,
        IOperation executableRoot)
    {
        return TryGetContainingAwaitedWhenAll(reference, out _, out _) ||
               !GetAwaitedWhenAllForStableArrayElement(reference, executableRoot)
                   .IsDefaultOrEmpty;
    }

    private static bool IsReferenceInAwaitedSingleTaskWhenAny(
        ILocalReferenceOperation reference)
    {
        return TryGetContainingAwaitedSingleTaskWhenAny(reference, out _, out _);
    }

    private static bool EscapesToUnknownConsumer(ILocalReferenceOperation reference)
    {
        IOperation current = reference;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        if (current.Parent is IArgumentOperation argument &&
            argument.Parent is IInvocationOperation invocation)
        {
            return !IsTaskWhenAll(invocation) &&
                   !IsTaskWhenAny(invocation) &&
                   !IsTaskWrapper(invocation, current);
        }

        if (current.Parent is ISimpleAssignmentOperation assignment)
            return assignment.Target.UnwrapConversions() is not IDiscardOperation;

        return current.Parent is IReturnOperation;
    }

    private static bool TryGetContainingAwaitedWhenAll(
        IOperation operation,
        out IInvocationOperation whenAll,
        out IAwaitOperation awaitOperation)
    {
        for (IOperation? current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IInvocationOperation invocation &&
                IsTaskWhenAll(invocation) &&
                TryGetImmediateAwait(invocation, out awaitOperation))
            {
                whenAll = invocation;
                return true;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        whenAll = null!;
        awaitOperation = null!;
        return false;
    }

    private static ImmutableArray<IAwaitOperation> GetAwaitedWhenAllForStableArrayElement(
        ILocalReferenceOperation elementReference,
        IOperation executableRoot)
    {
        var isArrayElement = false;
        IVariableDeclaratorOperation? declarator = null;
        for (IOperation? current = elementReference; current != null; current = current.Parent)
        {
            if (current is IArrayCreationOperation)
                isArrayElement = true;

            if (current is IVariableDeclaratorOperation variableDeclarator)
            {
                declarator = variableDeclarator;
                break;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        if (!isArrayElement ||
            declarator == null ||
            !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                declarator.Symbol,
                int.MaxValue,
                out var assignedValue,
            default) ||
            assignedValue.UnwrapConversions() is not IArrayCreationOperation)
        {
            return ImmutableArray<IAwaitOperation>.Empty;
        }

        if (executableRoot.Descendants()
            .OfType<ILocalReferenceOperation>()
            .Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.Local,
                    declarator.Symbol) &&
                IsInsideNestedExecutable(candidate, executableRoot)))
        {
            return ImmutableArray<IAwaitOperation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<IAwaitOperation>();
        var contentsAreStable = true;
        foreach (var candidate in EnumerateOutsideNestedExecutables(executableRoot)
                     .OfType<ILocalReferenceOperation>()
                     .Where(candidate =>
                         candidate.Syntax.SpanStart > declarator.Syntax.SpanStart &&
                         SymbolEqualityComparer.Default.Equals(
                             candidate.Local,
                             declarator.Symbol))
                     .OrderBy(candidate => candidate.Syntax.SpanStart))
        {
            if (!contentsAreStable)
                continue;

            if (!TryGetDirectTaskWhenAllInput(candidate, out var awaitOperation))
            {
                contentsAreStable = false;
                continue;
            }

            if (awaitOperation != null)
                builder.Add(awaitOperation);
        }

        return builder.ToImmutable();
    }

    private static bool TryGetDirectTaskWhenAllInput(
        ILocalReferenceOperation reference,
        out IAwaitOperation? awaitOperation)
    {
        IOperation current = reference;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
            current = current.Parent;

        if (current.Parent is not IArgumentOperation argument ||
            argument.Parent is not IInvocationOperation invocation ||
            !IsTaskWhenAll(invocation))
        {
            awaitOperation = null;
            return false;
        }

        if (!TryGetImmediateAwait(invocation, out var immediateAwait))
        {
            awaitOperation = null;
            return true;
        }

        awaitOperation = immediateAwait;
        return true;
    }

    private static bool TaskCompletionPointsEndOnEveryBranch(
        ImmutableArray<(SyntaxNode Completion, int ThrowingPrefixEnd)> completions,
        SyntaxNode taskStart,
        SyntaxNode current,
        IOperation executableRoot)
    {
        var visitedConditionals = new HashSet<SyntaxNode>();
        foreach (var completion in completions)
        {
            foreach (var ifStatement in completion.Completion.Ancestors().OfType<IfStatementSyntax>())
            {
                if (!visitedConditionals.Add(ifStatement) ||
                    ifStatement.Else == null ||
                    ifStatement.SpanStart >= current.SpanStart ||
                    !IsDefinitelyExecutedBefore(ifStatement, current, executableRoot.Syntax))
                {
                    continue;
                }

                var thenEndsTask = completions.Any(candidate =>
                    candidate.Completion.SpanStart < current.SpanStart &&
                    ifStatement.Statement.Span.Contains(candidate.Completion.Span) &&
                    IsUnconditionallyExecutedWithin(
                        candidate.Completion,
                        ifStatement.Statement) &&
                    !CompletionCanBeBypassedByContinuingHandler(
                        taskStart,
                        candidate.Completion,
                        candidate.ThrowingPrefixEnd,
                        current,
                        executableRoot));
                var elseEndsTask = completions.Any(candidate =>
                    candidate.Completion.SpanStart < current.SpanStart &&
                    ifStatement.Else.Statement.Span.Contains(candidate.Completion.Span) &&
                    IsUnconditionallyExecutedWithin(
                        candidate.Completion,
                        ifStatement.Else.Statement) &&
                    !CompletionCanBeBypassedByContinuingHandler(
                        taskStart,
                        candidate.Completion,
                        candidate.ThrowingPrefixEnd,
                        current,
                        executableRoot));
                if (thenEndsTask && elseEndsTask)
                    return true;
            }

            foreach (var switchStatement in completion.Completion.Ancestors()
                         .OfType<SwitchStatementSyntax>())
            {
                if (!visitedConditionals.Add(switchStatement) ||
                    !switchStatement.Sections.Any(section =>
                        section.Labels.Any(label => label.IsKind(
                            Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultSwitchLabel))) ||
                    switchStatement.SpanStart >= current.SpanStart ||
                    !IsDefinitelyExecutedBefore(switchStatement, current, executableRoot.Syntax))
                {
                    continue;
                }

                var everySectionEndsTask = switchStatement.Sections.All(section =>
                    completions.Any(candidate =>
                        candidate.Completion.SpanStart < current.SpanStart &&
                        section.Span.Contains(candidate.Completion.Span) &&
                        IsUnconditionallyExecutedWithin(
                            candidate.Completion,
                            section) &&
                        !CompletionCanBeBypassedByContinuingHandler(
                            taskStart,
                            candidate.Completion,
                            candidate.ThrowingPrefixEnd,
                            current,
                            executableRoot)));
                if (everySectionEndsTask)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetContainingAwaitedSingleTaskWhenAny(
        IOperation operation,
        out IInvocationOperation whenAny,
        out IAwaitOperation awaitOperation)
    {
        for (IOperation? current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IInvocationOperation invocation &&
                IsTaskWhenAny(invocation) &&
                TaskWhenAnyHasExactlyOneInput(invocation) &&
                TryGetImmediateAwait(invocation, out awaitOperation))
            {
                whenAny = invocation;
                return true;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        whenAny = null!;
        awaitOperation = null!;
        return false;
    }

    private static bool IsTaskWrapper(IInvocationOperation invocation, IOperation wrapped)
    {
        if (invocation.TargetMethod.Name is not ("ConfigureAwait" or "AsTask"))
            return false;

        var receiver = invocation.GetInvocationReceiver(false);
        return receiver != null &&
               (ReferenceEquals(receiver, wrapped) ||
                receiver.Syntax.Span.Equals(wrapped.Syntax.Span));
    }

    private static bool IsTaskWhenAll(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "WhenAll" &&
               invocation.TargetMethod.ContainingType.Name == "Task" &&
               invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool IsTaskWhenAny(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "WhenAny" &&
               invocation.TargetMethod.ContainingType.Name == "Task" &&
               invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool TaskWhenAnyHasExactlyOneInput(IInvocationOperation whenAny)
    {
        if (whenAny.Arguments.Length != 1)
            return false;

        var argument = whenAny.Arguments[0];
        var value = argument.Value.UnwrapConversions();
        if (value is IArrayCreationOperation arrayCreation &&
            arrayCreation.Initializer != null)
        {
            return arrayCreation.Initializer.ElementValues.Length == 1;
        }

        return argument.Parameter?.IsParams == true && ReturnsTaskLike(value.Type);
    }

    private static bool IsTaskCompletionWrapper(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.ContainingNamespace?.ToString() != "System.Threading.Tasks")
            return false;

        return method.Name switch
        {
            "ConfigureAwait" => method.ContainingType.Name is "Task" or "ValueTask",
            "AsTask" => method.ContainingType.Name == "ValueTask",
            _ => false
        };
    }

    private static bool IsTaskCompletedTaskProperty(IPropertyReferenceOperation propertyReference)
    {
        var property = propertyReference.Property;
        return property.Name == "CompletedTask" &&
               property.ContainingType.Name == "Task" &&
               property.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool TaskCombinatorInputsAreDefinitelyNonNull(
        IInvocationOperation combinator,
        IOperation executableRoot)
    {
        var visitedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return combinator.Arguments.All(argument =>
            IsDefinitelyNonNullTaskInput(
                argument.Value,
                executableRoot,
                combinator.Syntax.SpanStart,
                visitedLocals));
    }

    private static bool IsDefinitelyNonNullTaskInput(
        IOperation value,
        IOperation executableRoot,
        int beforePosition,
        HashSet<ISymbol> visitedLocals)
    {
        value = value.UnwrapConversions();
        switch (value)
        {
            case IArrayCreationOperation arrayCreation
                when arrayCreation.Initializer != null:
                return arrayCreation.Initializer.ElementValues.All(element =>
                    IsDefinitelyNonNullTaskInput(
                        element,
                        executableRoot,
                        beforePosition,
                        visitedLocals));

            case ILocalReferenceOperation localReference:
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalHasNoUntrackedWritesBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition) ||
                    !TryGetLatestDefiniteStorageReceiverValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue))
                {
                    return false;
                }

                var isDefinitelyNonNull = IsDefinitelyNonNullTaskInput(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    visitedLocals);
                visitedLocals.Remove(localReference.Local);
                return isDefinitelyNonNull;

            case IInvocationOperation invocation:
                if (TryClassifyEfAsyncOperation(
                        invocation,
                        executableRoot,
                        default,
                        out _))
                {
                    return true;
                }

                if (IsTaskCompletionWrapper(invocation) &&
                    invocation.TargetMethod.Name == "AsTask")
                {
                    return true;
                }

                return ReturnsTaskLike(invocation.Type) &&
                       invocation.TargetMethod.ContainingType.Name == "Task" &&
                       invocation.TargetMethod.ContainingNamespace?.ToString() ==
                       "System.Threading.Tasks";

            case IPropertyReferenceOperation propertyReference:
                return IsTaskCompletedTaskProperty(propertyReference);

            case IObjectCreationOperation objectCreation:
                return ReturnsTaskLike(objectCreation.Type);

            default:
                return false;
        }
    }

    private static bool IsDefinitelyExecutedBefore(
        SyntaxNode previous,
        SyntaxNode current,
        SyntaxNode executableRoot)
    {
        if (CanForwardGotoBypass(previous, current, executableRoot))
            return false;

        foreach (var ancestor in previous.Ancestors())
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Condition.Span.Contains(previous.Span))
                    continue;

                var previousBranch = GetIfBranch(ifStatement, previous);
                var currentBranch = GetIfBranch(ifStatement, current);
                if (previousBranch != null &&
                    currentBranch == null &&
                    OppositeBranchAlwaysExits(ifStatement, previousBranch))
                {
                    continue;
                }

                if (previousBranch == null ||
                    currentBranch == null ||
                    !ReferenceEquals(previousBranch, currentBranch))
                {
                    return false;
                }
            }
            else if (ancestor is ConditionalExpressionSyntax conditional)
            {
                if (conditional.Condition.Span.Contains(previous.Span))
                    continue;

                var previousBranch = conditional.WhenTrue.Span.Contains(previous.Span)
                    ? conditional.WhenTrue
                    : conditional.WhenFalse;
                if (!previousBranch.Span.Contains(current.Span))
                    return false;
            }
            else if (ancestor is BinaryExpressionSyntax binary &&
                     binary.Kind() is SyntaxKind.CoalesceExpression or
                         SyntaxKind.LogicalAndExpression or
                         SyntaxKind.LogicalOrExpression)
            {
                if (binary.Right.Span.Contains(previous.Span) &&
                    !binary.Right.Span.Contains(current.Span))
                {
                    return false;
                }
            }
            else if (ancestor is AssignmentExpressionSyntax assignment &&
                     assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                     assignment.Right.Span.Contains(previous.Span) &&
                     !assignment.Right.Span.Contains(current.Span))
            {
                return false;
            }
            else if (ancestor is SwitchSectionSyntax switchSection)
            {
                if (!switchSection.Span.Contains(current.Span))
                    return false;
            }
            else if (ancestor is SwitchExpressionArmSyntax switchArm)
            {
                if (!switchArm.Span.Contains(current.Span))
                    return false;
            }
            else if (ancestor is WhileStatementSyntax or
                     ForStatementSyntax or
                     ForEachStatementSyntax or
                     ForEachVariableStatementSyntax)
            {
                if (!ancestor.Span.Contains(current.Span))
                    return false;
            }
            else if (ancestor is CatchClauseSyntax)
            {
                if (!ancestor.Span.Contains(current.Span))
                    return false;
            }
        }

        return true;
    }

    private static bool CanForwardGotoBypass(
        SyntaxNode previous,
        SyntaxNode current,
        SyntaxNode executableRoot)
    {
        var labels = executableRoot.DescendantNodes()
            .OfType<LabeledStatementSyntax>()
            .Where(label => !IsInsideNestedExecutableSyntax(label, executableRoot))
            .ToArray();

        foreach (var gotoStatement in executableRoot.DescendantNodes()
                     .OfType<GotoStatementSyntax>())
        {
            if (gotoStatement.SpanStart >= previous.SpanStart ||
                !gotoStatement.IsKind(SyntaxKind.GotoStatement) ||
                gotoStatement.Expression is not IdentifierNameSyntax identifier ||
                IsInsideNestedExecutableSyntax(gotoStatement, executableRoot))
            {
                continue;
            }

            if (labels.Any(label =>
                    label.Identifier.ValueText == identifier.Identifier.ValueText &&
                    label.SpanStart > previous.SpanStart &&
                    label.SpanStart <= current.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? GetIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ifStatement.Statement.Span.Contains(node.Span))
            return ifStatement.Statement;

        if (ifStatement.Else?.Statement.Span.Contains(node.Span) == true)
            return ifStatement.Else.Statement;

        return null;
    }

    private static bool OppositeBranchAlwaysExits(
        IfStatementSyntax ifStatement,
        SyntaxNode previousBranch)
    {
        if (ReferenceEquals(previousBranch, ifStatement.Statement))
            return ifStatement.Else != null && BranchAlwaysExits(ifStatement.Else.Statement);

        return BranchAlwaysExits(ifStatement.Statement);
    }

    private static bool BranchAlwaysExits(StatementSyntax statement)
    {
        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;

            case BlockSyntax block when block.Statements.Count > 0:
                return BranchAlwaysExits(block.Statements[block.Statements.Count - 1]);

            case IfStatementSyntax nestedIf when nestedIf.Else != null:
                return BranchAlwaysExits(nestedIf.Statement) &&
                       BranchAlwaysExits(nestedIf.Else.Statement);

            default:
                return false;
        }
    }

    private static IEnumerable<IOperation> EnumerateOutsideNestedExecutables(IOperation root)
    {
        foreach (var operation in root.Descendants())
        {
            if (IsInsideNestedExecutable(operation, root))
                continue;

            yield return operation;
        }
    }

    private static bool IsInsideNestedExecutable(IOperation operation, IOperation root)
    {
        for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return true;
        }

        return false;
    }

    private static bool CanOperationRunBefore(
        IOperation operation,
        IOperation executableRoot,
        int beforePosition,
        bool useStableStorageReceiver = true)
    {
        return CanOperationRunBetween(
            operation,
            executableRoot,
            int.MinValue,
            beforePosition,
            useStableStorageReceiver);
    }

    private static bool CanOperationRunBetween(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        bool useStableStorageReceiver)
    {
        IOperation? nestedExecutable = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                nestedExecutable = parent;
                break;
            }
        }

        if (nestedExecutable == null)
        {
            return operation.Syntax.SpanStart > afterPosition &&
                   operation.Syntax.SpanStart < beforePosition;
        }

        if (nestedExecutable is ILocalFunctionOperation localFunction)
        {
            return EnumerateOutsideNestedExecutables(executableRoot).Any(candidate =>
                candidate.Syntax.SpanStart > afterPosition &&
                candidate.Syntax.SpanStart < beforePosition &&
                (candidate is IInvocationOperation invocation &&
                 SymbolEqualityComparer.Default.Equals(
                     invocation.TargetMethod.OriginalDefinition,
                     localFunction.Symbol.OriginalDefinition) ||
                 candidate is IMethodReferenceOperation methodReference &&
                 SymbolEqualityComparer.Default.Equals(
                     methodReference.Method.OriginalDefinition,
                     localFunction.Symbol.OriginalDefinition)));
        }

        for (IOperation? current = nestedExecutable.Parent;
             current != null && !ReferenceEquals(current, executableRoot);
             current = current.Parent)
        {
            if (current is IInvocationOperation directInvocation)
            {
                return directInvocation.Syntax.SpanStart > afterPosition &&
                       directInvocation.Syntax.SpanStart < beforePosition;
            }

            if (current is IVariableDeclaratorOperation declarator)
            {
                return EnumerateOutsideNestedExecutables(executableRoot)
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference =>
                        reference.Syntax.SpanStart > afterPosition &&
                        reference.Syntax.SpanStart < beforePosition &&
                        SymbolEqualityComparer.Default.Equals(
                            reference.Local,
                            declarator.Symbol));
            }

            if (current is ISimpleAssignmentOperation assignment)
            {
                if (!useStableStorageReceiver &&
                    TryGetStorageSymbol(assignment.Target, out var storageSymbol))
                {
                    return EnumerateOutsideNestedExecutables(executableRoot)
                        .OfType<IInvocationOperation>()
                        .Any(invocation =>
                            invocation.Syntax.SpanStart > assignment.Syntax.SpanStart &&
                            invocation.Syntax.SpanStart > afterPosition &&
                            invocation.Syntax.SpanStart < beforePosition &&
                            invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                            InvocationReceiverMatchesStorageSymbol(invocation, storageSymbol));
                }

                if (!TryGetStorageOrigin(
                        assignment.Target,
                        executableRoot,
                        assignment.Syntax.SpanStart,
                        out var storage))
                {
                    continue;
                }

                return EnumerateOutsideNestedExecutables(executableRoot)
                    .OfType<IInvocationOperation>()
                    .Any(invocation =>
                        invocation.Syntax.SpanStart > assignment.Syntax.SpanStart &&
                        invocation.Syntax.SpanStart > afterPosition &&
                        invocation.Syntax.SpanStart < beforePosition &&
                        invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                        InvocationReceiverMatchesStorage(
                            invocation,
                            executableRoot,
                            storage) &&
                        !StorageIsOverwrittenBetween(
                            executableRoot,
                            storage,
                            assignment.Syntax.SpanStart,
                            invocation.Syntax.SpanStart));
            }
        }

        return false;
    }

    private static bool TryGetStorageSymbol(
        IOperation target,
        out ISymbol storage)
    {
        target = target.UnwrapConversions();
        if (target is IFieldReferenceOperation fieldReference)
        {
            storage = fieldReference.Field;
            return true;
        }

        if (target is IPropertyReferenceOperation propertyReference)
        {
            storage = propertyReference.Property;
            return true;
        }

        storage = null!;
        return false;
    }

    private static bool InvocationReceiverMatchesStorageSymbol(
        IInvocationOperation invocation,
        ISymbol storage)
    {
        var receiver = invocation.Instance?.UnwrapConversions();
        return receiver is IFieldReferenceOperation fieldReference &&
               SymbolEqualityComparer.Default.Equals(fieldReference.Field, storage) ||
               receiver is IPropertyReferenceOperation propertyReference &&
               SymbolEqualityComparer.Default.Equals(propertyReference.Property, storage);
    }

    private static bool TryGetStorageOrigin(
        IOperation target,
        IOperation executableRoot,
        int beforePosition,
        out ContextOrigin storage)
    {
        target = target.UnwrapConversions();
        if (target is IFieldReferenceOperation fieldReference)
        {
            return TryCreateStorageOrigin(
                fieldReference.Field,
                fieldReference.Instance,
                executableRoot,
                beforePosition,
                out storage);
        }

        if (target is IPropertyReferenceOperation propertyReference)
        {
            return TryCreateStorageOrigin(
                propertyReference.Property,
                propertyReference.Instance,
                executableRoot,
                beforePosition,
                out storage);
        }

        storage = default;
        return false;
    }

    private static bool TryCreateStorageOrigin(
        ISymbol symbol,
        IOperation? instance,
        IOperation executableRoot,
        int beforePosition,
        out ContextOrigin storage)
    {
        if (instance == null)
        {
            storage = new ContextOrigin(symbol, symbol.Name);
            return true;
        }

        if (TryResolveStorageReceiverSymbol(
                instance,
                executableRoot,
                beforePosition,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out var receiver))
        {
            storage = new ContextOrigin(symbol, receiver, symbol.Name);
            return true;
        }

        storage = default;
        return false;
    }

    private static bool TryResolveStorageReceiverSymbol(
        IOperation instance,
        IOperation executableRoot,
        int beforePosition,
        HashSet<ISymbol> visitedLocals,
        out ISymbol receiver)
    {
        instance = instance.UnwrapConversions();
        switch (instance)
        {
            case IParameterReferenceOperation parameterReference:
                if (StorageReceiverParameterHasNoWritesBefore(
                        executableRoot,
                        parameterReference.Parameter,
                        beforePosition))
                {
                    receiver = parameterReference.Parameter;
                    return true;
                }

                if (!TryGetLatestDefiniteStorageReceiverValueBefore(
                        executableRoot,
                        parameterReference.Parameter,
                        beforePosition,
                        out var parameterValue,
                        out var parameterAssignmentPosition))
                {
                    receiver = null!;
                    return false;
                }

                return TryResolveStorageReceiverSymbol(
                    parameterValue,
                    executableRoot,
                    parameterAssignmentPosition,
                    visitedLocals,
                    out receiver);

            case ILocalReferenceOperation localReference:
                if (localReference.Local.RefKind != RefKind.None ||
                    !visitedLocals.Add(localReference.Local))
                {
                    receiver = null!;
                    return false;
                }

                if (!StorageReceiverLocalHasNoUntrackedWritesBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition) ||
                    !TryGetLatestDefiniteStorageReceiverValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        out var localAssignmentPosition))
                {
                    visitedLocals.Remove(localReference.Local);
                    receiver = null!;
                    return false;
                }

                visitedLocals.Remove(localReference.Local);
                if (assignedValue.UnwrapConversions() is IObjectCreationOperation)
                {
                    receiver = localReference.Local;
                    return true;
                }

                return TryResolveStorageReceiverSymbol(
                    assignedValue,
                    executableRoot,
                    localAssignmentPosition,
                    visitedLocals,
                    out receiver);

            case IInstanceReferenceOperation instanceReference when instanceReference.Type != null:
                receiver = instanceReference.Type;
                return true;
        }

        receiver = null!;
        return false;
    }

    private static bool TryGetLatestDefiniteStorageReceiverValueBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition,
        out IOperation value)
    {
        return TryGetLatestDefiniteStorageReceiverValueBefore(
            executableRoot,
            local,
            beforePosition,
            out value,
            out _);
    }

    private static bool TryGetLatestDefiniteStorageReceiverValueBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition,
        out IOperation value,
        out int assignmentPosition)
    {
        value = null!;
        assignmentPosition = -1;
        LocalAssignment? latest = null;
        foreach (var assignment in LocalAssignmentCache.GetAssignments(executableRoot, local))
        {
            if (assignment.SpanStart < beforePosition &&
                (latest == null || assignment.SpanStart > latest.Value.SpanStart))
            {
                latest = assignment;
            }
        }

        if (latest == null ||
            !IsUnconditionallyExecutedWithin(latest.Value.Value.Syntax, executableRoot.Syntax))
        {
            return false;
        }

        value = latest.Value.Value.UnwrapConversions();
        assignmentPosition = latest.Value.SpanStart;
        return true;
    }

    private static bool StorageReceiverParameterHasNoWritesBefore(
        IOperation executableRoot,
        IParameterSymbol parameter,
        int beforePosition)
    {
        return !executableRoot.Descendants().Any(operation =>
            CanOperationRunBefore(operation, executableRoot, beforePosition, false) &&
            (operation is ISimpleAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is not IFieldReferenceOperation &&
             assignment.Target.UnwrapConversions() is not IPropertyReferenceOperation &&
             assignment.Target.ReferencesParameter(parameter) ||
             operation is IVariableDeclaratorOperation declarator &&
             declarator.Symbol.RefKind != RefKind.None &&
             declarator.Initializer?.Value.ReferencesParameter(parameter) == true ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.ReferencesParameter(parameter)));
    }

    private static bool TryGetLatestDefiniteStorageReceiverValueBefore(
        IOperation executableRoot,
        IParameterSymbol parameter,
        int beforePosition,
        out IOperation value,
        out int assignmentPosition)
    {
        value = null!;
        assignmentPosition = -1;
        foreach (var operation in executableRoot.Descendants())
        {
            if (!CanOperationRunBefore(operation, executableRoot, beforePosition, false))
                continue;

            if (operation is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is IParameterReferenceOperation target &&
                SymbolEqualityComparer.Default.Equals(target.Parameter, parameter))
            {
                if (!IsUnconditionallyExecutedWithin(assignment.Syntax, executableRoot.Syntax))
                    return false;

                if (assignment.Syntax.SpanStart > assignmentPosition)
                {
                    value = assignment.Value.UnwrapConversions();
                    assignmentPosition = assignment.Syntax.SpanStart;
                }

                continue;
            }

            if (operation is IAssignmentOperation otherAssignment &&
                otherAssignment.Target.UnwrapConversions() is not IFieldReferenceOperation &&
                otherAssignment.Target.UnwrapConversions() is not IPropertyReferenceOperation &&
                otherAssignment.Target.ReferencesParameter(parameter) ||
                operation is IVariableDeclaratorOperation declarator &&
                declarator.Symbol.RefKind != RefKind.None &&
                declarator.Initializer?.Value.ReferencesParameter(parameter) == true ||
                operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesParameter(parameter))
            {
                return false;
            }
        }

        return assignmentPosition >= 0;
    }

    private static bool StorageReceiverLocalHasNoUntrackedWritesBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition)
    {
        return !executableRoot.Descendants().Any(operation =>
            CanOperationRunBefore(operation, executableRoot, beforePosition, false) &&
            (operation is ISimpleAssignmentOperation assignment &&
             IsInsideNestedExecutable(assignment, executableRoot) &&
             assignment.Target.UnwrapConversions() is not IFieldReferenceOperation &&
             assignment.Target.UnwrapConversions() is not IPropertyReferenceOperation &&
             assignment.Target.ReferencesLocal(local) ||
             operation is IAssignmentOperation nonSimpleAssignment &&
             operation is not ISimpleAssignmentOperation &&
             nonSimpleAssignment.Target.ReferencesLocal(local) ||
             operation is IVariableDeclaratorOperation declarator &&
             declarator.Symbol.RefKind != RefKind.None &&
             declarator.Initializer?.Value.ReferencesLocal(local) == true ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.ReferencesLocal(local)));
    }

    private static bool InvocationReceiverMatchesStorage(
        IInvocationOperation invocation,
        IOperation executableRoot,
        ContextOrigin storage)
    {
        var receiver = invocation.Instance?.UnwrapConversions();
        if (receiver == null)
            return false;

        if (TryGetStorageOrigin(
                receiver,
                executableRoot,
                invocation.Syntax.SpanStart,
                out var invokedStorage))
        {
            return ContextOriginComparer.Instance.Equals(storage, invokedStorage);
        }

        var instance = receiver switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Instance,
            IPropertyReferenceOperation propertyReference => propertyReference.Instance,
            _ => null
        };
        return storage.ReceiverSymbol != null &&
               TryGetStorageSymbol(receiver, out var invokedSymbol) &&
               SymbolEqualityComparer.Default.Equals(storage.Symbol, invokedSymbol) &&
               instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
               StorageReceiverLocalMayMatchOriginBefore(
                   executableRoot,
                   localReference.Local,
                   invocation.Syntax.SpanStart,
                   storage.ReceiverSymbol);
    }

    private static bool StorageReceiverLocalMayMatchOriginBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition,
        ISymbol expectedReceiver)
    {
        if (local.RefKind != RefKind.None)
            return true;

        if (!StorageReceiverLocalHasNoUntrackedWritesBefore(
                executableRoot,
                local,
                beforePosition))
        {
            return true;
        }

        var hasReachingValue = false;
        foreach (var assignment in LocalAssignmentCache.GetAssignments(executableRoot, local)
                     .Where(candidate => candidate.SpanStart < beforePosition)
                     .OrderBy(candidate => candidate.SpanStart))
        {
            var canMatch = !TryResolveStorageReceiverSymbol(
                               assignment.Value,
                               executableRoot,
                               assignment.SpanStart,
                               new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                               out var receiver) ||
                           SymbolEqualityComparer.Default.Equals(expectedReceiver, receiver);
            if (IsUnconditionallyExecutedWithin(
                    assignment.Value.Syntax,
                    executableRoot.Syntax))
            {
                hasReachingValue = canMatch;
            }
            else if (canMatch)
            {
                hasReachingValue = true;
            }
        }

        return hasReachingValue;
    }

    private static bool IsOperationDefinitelyExecutedBetween(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition)
    {
        return TryGetDefiniteExecutionPosition(
            operation,
            executableRoot,
            afterPosition,
            beforePosition,
            new HashSet<SyntaxNode>(),
            out _);
    }

    private static bool TryGetDefiniteExecutionPosition(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        HashSet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        IOperation? nestedExecutable = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                nestedExecutable = parent;
                break;
            }
        }

        if (nestedExecutable == null)
        {
            executionPosition = operation.Syntax.SpanStart;
            return executionPosition > afterPosition &&
                   executionPosition < beforePosition &&
                   IsUnconditionallyExecutedWithin(operation.Syntax, executableRoot.Syntax);
        }

        var nestedBody = nestedExecutable switch
        {
            ILocalFunctionOperation localFunction => localFunction.Body?.Syntax ?? localFunction.Syntax,
            IAnonymousFunctionOperation anonymousFunction => anonymousFunction.Body.Syntax,
            _ => nestedExecutable.Syntax
        };
        var isAsync = nestedExecutable is ILocalFunctionOperation { Symbol.IsAsync: true } or
            IAnonymousFunctionOperation { Symbol.IsAsync: true };
        if (isAsync ||
            nestedExecutable.Syntax.DescendantNodes().Any(node => node is YieldStatementSyntax) ||
            !IsUnconditionallyExecutedWithin(operation.Syntax, nestedBody) ||
            EnumerateOutsideNestedExecutables(nestedExecutable).Any(candidate =>
                !candidate.IsImplicit &&
                candidate.Syntax.SpanStart < operation.Syntax.SpanStart &&
                candidate is IReturnOperation or IThrowOperation))
        {
            executionPosition = -1;
            return false;
        }

        if (!visitedExecutables.Add(nestedExecutable.Syntax))
        {
            executionPosition = -1;
            return false;
        }

        try
        {
            if (nestedExecutable is ILocalFunctionOperation localFunction)
            {
                foreach (var invocation in executableRoot.Descendants().OfType<IInvocationOperation>())
                {
                    if (SymbolEqualityComparer.Default.Equals(
                            invocation.TargetMethod.OriginalDefinition,
                            localFunction.Symbol.OriginalDefinition) &&
                        TryGetDefiniteExecutionPosition(
                            invocation,
                            executableRoot,
                            afterPosition,
                            beforePosition,
                            visitedExecutables,
                            out executionPosition))
                    {
                        return true;
                    }
                }

                foreach (var methodReference in executableRoot.Descendants()
                             .OfType<IMethodReferenceOperation>())
                {
                    if (SymbolEqualityComparer.Default.Equals(
                            methodReference.Method.OriginalDefinition,
                            localFunction.Symbol.OriginalDefinition) &&
                        TryGetDelegateValueExecutionPosition(
                            methodReference,
                            executableRoot,
                            afterPosition,
                            beforePosition,
                            visitedExecutables,
                            out executionPosition))
                    {
                        return true;
                    }
                }

                executionPosition = -1;
                return false;
            }

            return TryGetDelegateValueExecutionPosition(
                nestedExecutable,
                executableRoot,
                afterPosition,
                beforePosition,
                visitedExecutables,
                out executionPosition);
        }
        finally
        {
            visitedExecutables.Remove(nestedExecutable.Syntax);
        }
    }

    private static bool TryGetDelegateValueExecutionPosition(
        IOperation delegateValue,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        HashSet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        if (!IsDelegateValueDefinitelySelected(delegateValue, executableRoot))
        {
            executionPosition = -1;
            return false;
        }

        for (IOperation? current = delegateValue.Parent;
             current != null && !ReferenceEquals(current, executableRoot);
             current = current.Parent)
        {
            if (current is IInvocationOperation currentInvocation)
            {
                if (currentInvocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                    currentInvocation.Instance?.Syntax.Span.Contains(delegateValue.Syntax.Span) == true)
                {
                    return TryGetDefiniteExecutionPosition(
                        currentInvocation,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }

                executionPosition = -1;
                return false;
            }

            if (current is IVariableDeclaratorOperation declarator)
            {
                if (!TryGetDefiniteExecutionPosition(
                        declarator,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out var assignmentExecutionPosition))
                {
                    executionPosition = -1;
                    return false;
                }

                return TryGetLocalDelegateExecutionPosition(
                    declarator.Symbol,
                    assignmentExecutionPosition,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    visitedExecutables,
                    out executionPosition);
            }

            if (current is ISimpleAssignmentOperation assignment)
            {
                if (!TryGetDefiniteExecutionPosition(
                        assignment,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out var assignmentExecutionPosition))
                {
                    executionPosition = -1;
                    return false;
                }

                var target = assignment.Target.UnwrapConversions();
                if (target is ILocalReferenceOperation localReference)
                {
                    return TryGetLocalDelegateExecutionPosition(
                        localReference.Local,
                        assignmentExecutionPosition,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }

                if (StorageReceiverIsMutatedEarlierInNestedExecutable(
                        target,
                        assignment,
                        executableRoot))
                {
                    executionPosition = -1;
                    return false;
                }

                if (TryGetStorageOrigin(
                        target,
                        executableRoot,
                        assignmentExecutionPosition,
                        out var storage))
                {
                    foreach (var invocation in executableRoot.Descendants()
                                 .OfType<IInvocationOperation>())
                    {
                        if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                            InvocationReceiverMatchesStorage(
                                invocation,
                                executableRoot,
                                storage) &&
                            TryGetDefiniteExecutionPosition(
                                invocation,
                                executableRoot,
                                afterPosition > assignmentExecutionPosition
                                    ? afterPosition
                                    : assignmentExecutionPosition,
                                beforePosition,
                                visitedExecutables,
                                out executionPosition) &&
                            !StorageIsOverwrittenBetween(
                                executableRoot,
                                storage,
                                assignmentExecutionPosition,
                                executionPosition))
                        {
                            return true;
                        }
                    }
                }

                executionPosition = -1;
                return false;
            }
        }

        executionPosition = -1;
        return false;
    }

    private static bool IsDelegateValueDefinitelySelected(
        IOperation delegateValue,
        IOperation executableRoot)
    {
        var valueSpan = delegateValue.Syntax.Span;
        for (var current = delegateValue.Syntax.Parent;
             current != null;
             current = current.Parent)
        {
            if (ReferenceEquals(current, executableRoot.Syntax))
                return true;

            if (current is BinaryExpressionSyntax binary &&
                binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression) &&
                binary.Right.Span.Contains(valueSpan) ||
                current is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceAssignmentExpression) &&
                assignment.Right.Span.Contains(valueSpan) ||
                current is ConditionalExpressionSyntax conditional &&
                !conditional.Condition.Span.Contains(valueSpan) ||
                current is SwitchExpressionArmSyntax or SwitchSectionSyntax or CatchClauseSyntax)
            {
                return false;
            }

            if (current is IfStatementSyntax ifStatement &&
                !ifStatement.Condition.Span.Contains(valueSpan) ||
                current is WhileStatementSyntax whileStatement &&
                !whileStatement.Condition.Span.Contains(valueSpan) ||
                current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetLocalDelegateExecutionPosition(
        ILocalSymbol local,
        int assignmentPosition,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        HashSet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        foreach (var invocation in executableRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                invocation.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
                SymbolEqualityComparer.Default.Equals(localReference.Local, local) &&
                TryGetDefiniteExecutionPosition(
                    invocation,
                    executableRoot,
                    afterPosition > assignmentPosition
                        ? afterPosition
                        : assignmentPosition,
                    beforePosition,
                    visitedExecutables,
                    out executionPosition) &&
                !LocalDelegateIsOverwrittenBetween(
                    executableRoot,
                    local,
                    assignmentPosition,
                    executionPosition))
            {
                return true;
            }
        }

        executionPosition = -1;
        return false;
    }

    private static bool LocalDelegateIsOverwrittenBetween(
        IOperation executableRoot,
        ILocalSymbol local,
        int assignmentPosition,
        int invocationPosition)
    {
        return executableRoot.Descendants().Any(operation =>
            IsOperationDefinitelyExecutedBetween(
                operation,
                executableRoot,
                assignmentPosition,
                invocationPosition) &&
            (operation is ISimpleAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static bool StorageIsOverwrittenBetween(
        IOperation executableRoot,
        ContextOrigin storage,
        int assignmentPosition,
        int invocationPosition)
    {
        foreach (var assignment in executableRoot.Descendants().OfType<IAssignmentOperation>())
        {
            if (!AssignmentDefinitelyReplacesDelegate(assignment))
                continue;

            var searchAfter = assignmentPosition;
            while (TryGetDefiniteExecutionPosition(
                       assignment,
                       executableRoot,
                       searchAfter,
                       invocationPosition,
                       new HashSet<SyntaxNode>(),
                       out var executionPosition))
            {
                if (!StorageReceiverIsMutatedEarlierInNestedExecutable(
                        assignment.Target,
                        assignment,
                        executableRoot) &&
                    StorageExpressionMatchesOriginAtPosition(
                        assignment.Target,
                        executableRoot,
                        executionPosition,
                        storage) &&
                    !StorageMayBeWrittenBetween(
                        executableRoot,
                        storage,
                        executionPosition,
                        invocationPosition))
                {
                    return true;
                }

                if (executionPosition <= searchAfter)
                    break;

                searchAfter = executionPosition;
            }
        }

        foreach (var argument in executableRoot.Descendants().OfType<IArgumentOperation>())
        {
            if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out))
                continue;

            if (!RefArgumentDefinitelyReplacesDelegate(argument))
                continue;

            var searchAfter = assignmentPosition;
            while (TryGetDefiniteExecutionPosition(
                       argument,
                       executableRoot,
                       searchAfter,
                       invocationPosition,
                       new HashSet<SyntaxNode>(),
                       out var executionPosition))
            {
                if (!StorageReceiverIsMutatedEarlierInNestedExecutable(
                        argument.Value,
                        argument,
                        executableRoot) &&
                    TryGetStorageOrigin(
                        argument.Value,
                        executableRoot,
                        executionPosition,
                        out var passedStorage) &&
                    ContextOriginComparer.Instance.Equals(storage, passedStorage) &&
                    !StorageMayBeWrittenBetween(
                        executableRoot,
                        storage,
                        executionPosition,
                        invocationPosition))
                {
                    return true;
                }

                if (executionPosition <= searchAfter)
                    break;

                searchAfter = executionPosition;
            }
        }

        return false;
    }

    private static bool StorageMayBeWrittenBetween(
        IOperation executableRoot,
        ContextOrigin storage,
        int afterPosition,
        int beforePosition)
    {
        if (executableRoot.Descendants().Any(operation =>
                OperationMayExecuteUserCode(operation) &&
                CanOperationRunBetween(
                    operation,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    true)))
        {
            return true;
        }

        foreach (var assignment in executableRoot.Descendants().OfType<IAssignmentOperation>())
        {
            if (CanOperationRunBetween(
                    assignment,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    true) &&
                (StorageExpressionMatchesOriginAtPosition(
                     assignment.Target,
                     executableRoot,
                     assignment.Syntax.SpanStart,
                     storage) ||
                 TryGetStorageSymbol(assignment.Target, out var writtenSymbol) &&
                 SymbolEqualityComparer.Default.Equals(storage.Symbol, writtenSymbol)))
            {
                return true;
            }
        }

        foreach (var argument in executableRoot.Descendants().OfType<IArgumentOperation>())
        {
            if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out) ||
                !CanOperationRunBetween(
                    argument,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    true) ||
                !(TryGetStorageOrigin(
                      argument.Value,
                      executableRoot,
                      argument.Syntax.SpanStart,
                      out var passedStorage) &&
                  ContextOriginComparer.Instance.Equals(storage, passedStorage) ||
                  TryGetStorageSymbol(argument.Value, out var passedSymbol) &&
                  SymbolEqualityComparer.Default.Equals(storage.Symbol, passedSymbol)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool OperationMayExecuteUserCode(IOperation operation)
    {
        return operation is IInvocationOperation ||
               operation is IPropertyReferenceOperation ||
               operation is IEventAssignmentOperation ||
               operation is IObjectCreationOperation ||
               operation is IDynamicInvocationOperation ||
               operation is IConversionOperation { OperatorMethod: not null } ||
               operation is IBinaryOperation { OperatorMethod: not null } ||
               operation is IUnaryOperation { OperatorMethod: not null } ||
               operation is IIncrementOrDecrementOperation { OperatorMethod: not null } ||
               operation is ICompoundAssignmentOperation { OperatorMethod: not null };
    }

    private static bool AssignmentDefinitelyReplacesDelegate(
        IAssignmentOperation assignment)
    {
        if (!StorageTargetHasReliableDirectSetter(assignment.Target))
            return false;

        var value = assignment.Value.UnwrapConversions();
        if (value.ConstantValue is { HasValue: true, Value: null })
            return true;

        var anonymousFunction = value switch
        {
            IAnonymousFunctionOperation anonymous => anonymous,
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation anonymous } => anonymous,
            _ => null
        };
        return anonymousFunction?.Syntax is AnonymousFunctionExpressionSyntax syntax &&
               syntax.ChildNodes()
                   .OfType<BlockSyntax>()
                   .Any(block => block.Statements.Count == 0);
    }

    private static bool StorageTargetHasReliableDirectSetter(IOperation target)
    {
        target = target.UnwrapConversions();
        if (target is IFieldReferenceOperation)
            return true;

        if (target is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Property.ContainingType.TypeKind != TypeKind.Class ||
            propertyReference.Property.IsAbstract ||
            propertyReference.Property.IsVirtual ||
            propertyReference.Property.IsOverride)
        {
            return false;
        }

        foreach (var syntaxReference in propertyReference.Property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.AccessorList == null)
            {
                continue;
            }

            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if ((accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration) ||
                     accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration)) &&
                    accessor.Body == null &&
                    accessor.ExpressionBody == null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RefArgumentDefinitelyReplacesDelegate(
        IArgumentOperation argument)
    {
        if (argument.Parent is not IInvocationOperation invocation ||
            argument.Parameter == null)
        {
            return false;
        }

        foreach (var syntaxReference in invocation.TargetMethod.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not MethodDeclarationSyntax declaration)
                continue;

            if (declaration.ExpressionBody?.Expression is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == argument.Parameter.Name &&
                assignment.Right is AnonymousFunctionExpressionSyntax anonymousFunction &&
                anonymousFunction.ChildNodes()
                    .OfType<BlockSyntax>()
                    .Any(block => block.Statements.Count == 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StorageExpressionMatchesOriginAtPosition(
        IOperation storageExpression,
        IOperation executableRoot,
        int executionPosition,
        ContextOrigin expectedStorage)
    {
        if (TryGetStorageOrigin(
                storageExpression,
                executableRoot,
                executionPosition,
                out var storage) &&
            ContextOriginComparer.Instance.Equals(expectedStorage, storage))
        {
            return true;
        }

        storageExpression = storageExpression.UnwrapConversions();
        var instance = storageExpression switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Instance,
            IPropertyReferenceOperation propertyReference => propertyReference.Instance,
            _ => null
        };
        var symbol = storageExpression switch
        {
            IFieldReferenceOperation fieldReference => (ISymbol)fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };
        if (symbol != null &&
            instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
            StorageReceiverLocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                executionPosition) &&
            TryGetLatestDefiniteStorageReceiverValueBefore(
                executableRoot,
                localReference.Local,
                executionPosition,
                out var assignedValue) &&
            TryResolveStorageReceiverSymbol(
                assignedValue,
                executableRoot,
                executionPosition,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out var receiver))
        {
            var candidate = new ContextOrigin(symbol, receiver, symbol.Name);
            return ContextOriginComparer.Instance.Equals(expectedStorage, candidate);
        }

        return false;
    }

    private static bool StorageReceiverIsMutatedEarlierInNestedExecutable(
        IOperation storageExpression,
        IOperation operation,
        IOperation executableRoot)
    {
        storageExpression = storageExpression.UnwrapConversions();
        var instance = storageExpression switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Instance,
            IPropertyReferenceOperation propertyReference => propertyReference.Instance,
            _ => null
        };
        instance = instance?.UnwrapConversions();
        ISymbol? receiver = instance switch
        {
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            _ => null
        };
        if (receiver == null)
            return false;

        IOperation? nestedExecutable = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                nestedExecutable = parent;
                break;
            }
        }

        if (nestedExecutable == null)
            return false;

        return EnumerateOutsideNestedExecutables(nestedExecutable).Any(candidate =>
            candidate.Syntax.SpanStart < operation.Syntax.SpanStart &&
            (receiver is ILocalSymbol local &&
             (candidate is IAssignmentOperation assignment &&
              assignment.Target.UnwrapConversions() is ILocalReferenceOperation writtenLocal &&
              SymbolEqualityComparer.Default.Equals(writtenLocal.Local, local) ||
              candidate is IArgumentOperation argument &&
              argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
              argument.Value.ReferencesLocal(local)) ||
             receiver is IParameterSymbol parameter &&
             (candidate is IAssignmentOperation parameterAssignment &&
              parameterAssignment.Target.UnwrapConversions() is IParameterReferenceOperation writtenParameter &&
              SymbolEqualityComparer.Default.Equals(writtenParameter.Parameter, parameter) ||
              candidate is IArgumentOperation parameterArgument &&
              parameterArgument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
              parameterArgument.Value.ReferencesParameter(parameter))));
    }

    private static void AnalyzeSelectorFanOut(
        IOperation executableRoot,
        OperationBlockAnalysisContext context)
    {
        foreach (var whenAll in EnumerateOutsideNestedExecutables(executableRoot).OfType<IInvocationOperation>())
        {
            if (!IsTaskWhenAll(whenAll) ||
                whenAll.Arguments.Length != 1 ||
                !TryGetArgumentValue(whenAll, 0, out var whenAllInput) ||
                whenAllInput.UnwrapConversions() is not IInvocationOperation select ||
                !IsEnumerableSelect(select) ||
                !TryGetArgumentValue(select, 0, out var source) ||
                !TryGetArgumentValue(select, 1, out var selectorValue) ||
                SourceIsAtMostOne(
                    source,
                    executableRoot,
                    select.Syntax.SpanStart,
                    context.CancellationToken,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)) ||
                !TryGetAnonymousFunction(selectorValue, out var selector))
            {
                continue;
            }

            EfOperation? capturedOperation = null;
            foreach (var invocation in EnumerateOutsideNestedExecutables(selector.Body)
                         .OfType<IInvocationOperation>())
            {
                if (!TryClassifyEfAsyncOperation(
                        invocation,
                        executableRoot,
                        context.CancellationToken,
                        out var operation) ||
                    !IsUnconditionallyExecutedWithin(
                        invocation.Syntax,
                        selector.Body.Syntax) ||
                    IsOriginDeclaredInside(operation.Origin, selector.Syntax) ||
                    IsSynchronouslyCompletedWithinSelector(
                        invocation,
                        selector))
                {
                    continue;
                }

                capturedOperation = operation;
                break;
            }

            if (!capturedOperation.HasValue)
                continue;

            var captured = capturedOperation.Value;
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    whenAll.Syntax.GetLocation(),
                    new[] { captured.Invocation.Syntax.GetLocation() },
                    properties: ImmutableDictionary<string, string?>.Empty,
                    captured.Origin.DisplayName));
        }
    }

    private static bool IsSynchronouslyCompletedWithinSelector(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation selector)
    {
        if (TryGetSynchronousTaskCompletion(invocation, out _))
            return true;

        if (!TryGetAssignedTaskLocal(invocation, out var taskLocal))
            return false;

        return IsStableTaskLocalSynchronouslyCompleted(
            taskLocal,
            invocation.Syntax.SpanStart,
            selector,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsStableTaskLocalSynchronouslyCompleted(
        ILocalSymbol taskLocal,
        int afterPosition,
        IAnonymousFunctionOperation selector,
        ISet<ILocalSymbol> visited)
    {
        if (!visited.Add(taskLocal))
            return false;

        foreach (var reference in EnumerateOutsideNestedExecutables(selector.Body)
                     .OfType<ILocalReferenceOperation>()
                     .Where(reference =>
                         SymbolEqualityComparer.Default.Equals(reference.Local, taskLocal) &&
                         reference.Syntax.SpanStart > afterPosition))
        {
            if (TryGetSynchronousTaskCompletion(reference, out var completion) &&
                IsUnconditionallyExecutedWithin(
                    completion.Syntax,
                    selector.Body.Syntax) &&
                SelectorLocalRemainsStable(
                    selector.Body,
                    taskLocal,
                    afterPosition,
                    completion.Syntax.SpanStart))
            {
                return true;
            }
        }

        foreach (var declarator in EnumerateOutsideNestedExecutables(selector.Body)
                     .OfType<IVariableDeclaratorOperation>())
        {
            if (declarator.Syntax.SpanStart <= afterPosition ||
                declarator.Initializer == null ||
                !IsUnconditionallyExecutedWithin(
                    declarator.Syntax,
                    selector.Body.Syntax) ||
                !TryGetStableLocalReference(
                    declarator.Initializer.Value,
                    out var sourceLocal) ||
                !SymbolEqualityComparer.Default.Equals(sourceLocal, taskLocal) ||
                !SelectorLocalRemainsStable(
                    selector.Body,
                    taskLocal,
                    afterPosition,
                    declarator.Syntax.SpanStart))
            {
                continue;
            }

            if (IsStableTaskLocalSynchronouslyCompleted(
                    declarator.Symbol,
                    declarator.Syntax.SpanStart,
                    selector,
                    visited))
            {
                return true;
            }
        }

        foreach (var assignment in EnumerateOutsideNestedExecutables(selector.Body)
                     .OfType<ISimpleAssignmentOperation>())
        {
            if (assignment.Syntax.SpanStart <= afterPosition ||
                assignment.Target.UnwrapConversions() is not ILocalReferenceOperation target ||
                !IsStandaloneAssignment(assignment) ||
                !IsUnconditionallyExecutedWithin(
                    assignment.Syntax,
                    selector.Body.Syntax) ||
                !TryGetStableLocalReference(assignment.Value, out var sourceLocal) ||
                !SymbolEqualityComparer.Default.Equals(sourceLocal, taskLocal) ||
                !SelectorLocalRemainsStable(
                    selector.Body,
                    taskLocal,
                    afterPosition,
                    assignment.Syntax.SpanStart))
            {
                continue;
            }

            if (IsStableTaskLocalSynchronouslyCompleted(
                    target.Local,
                    assignment.Syntax.SpanStart,
                    selector,
                    visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SelectorLocalRemainsStable(
        IOperation selectorBody,
        ILocalSymbol local,
        int afterPosition,
        int beforePosition)
    {
        foreach (var operation in selectorBody.Descendants())
        {
            if (!CanOperationOrNestedExecutableRunBetween(
                    operation,
                    selectorBody,
                    afterPosition,
                    beforePosition))
            {
                continue;
            }

            if (operation is IDynamicInvocationOperation dynamicInvocation &&
                dynamicInvocation.ReferencesLocal(local))
            {
                return false;
            }

            if (operation is IAssignmentOperation assignment &&
                assignment.Target.ReferencesLocal(local))
            {
                return false;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Symbol.RefKind != RefKind.None &&
                declarator.Initializer?.Value.ReferencesLocal(local) == true)
            {
                return false;
            }

            if (operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesLocal(local))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanOperationOrNestedExecutableRunBetween(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition)
    {
        if (!IsInsideNestedExecutable(operation, executableRoot))
        {
            return CanOperationRunBetween(
                operation,
                executableRoot,
                afterPosition,
                beforePosition,
                useStableStorageReceiver: false);
        }

        return CanNestedExecutableOperationRunBetween(
            operation,
            executableRoot,
            afterPosition,
            beforePosition,
            new HashSet<SyntaxNode>(),
            out _);
    }

    private static bool CanNestedExecutableOperationRunBetween(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        ISet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        IOperation? nestedExecutable = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                nestedExecutable = parent;
                break;
            }
        }

        if (nestedExecutable != null)
        {
            return CanNestedExecutableRunBetween(
                nestedExecutable,
                executableRoot,
                afterPosition,
                beforePosition,
                visitedExecutables,
                out executionPosition);
        }

        executionPosition = -1;
        return false;
    }

    private static bool CanNestedExecutableRunBetween(
        IOperation nestedExecutable,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        ISet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        if (!visitedExecutables.Add(nestedExecutable.Syntax))
        {
            executionPosition = -1;
            return false;
        }

        try
        {
            if (nestedExecutable is ILocalFunctionOperation localFunction)
            {
                foreach (var candidate in executableRoot.Descendants())
                {
                    if (candidate is IInvocationOperation invocation &&
                        SymbolEqualityComparer.Default.Equals(
                            invocation.TargetMethod.OriginalDefinition,
                            localFunction.Symbol.OriginalDefinition) &&
                        NestedExecutableTriggerCanRunBetween(
                            candidate,
                            executableRoot,
                            afterPosition,
                            beforePosition,
                            visitedExecutables,
                            out executionPosition) ||
                        candidate is IMethodReferenceOperation methodReference &&
                        SymbolEqualityComparer.Default.Equals(
                            methodReference.Method.OriginalDefinition,
                            localFunction.Symbol.OriginalDefinition) &&
                        SelectorCallbackValueCanRunBetween(
                        candidate,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition))
                    {
                        return true;
                    }
                }

                executionPosition = -1;
                return false;
            }

            if (nestedExecutable is not IAnonymousFunctionOperation)
            {
                executionPosition = -1;
                return false;
            }

            return SelectorCallbackValueCanRunBetween(
                nestedExecutable,
                executableRoot,
                afterPosition,
                beforePosition,
                visitedExecutables,
                out executionPosition);
        }
        finally
        {
            visitedExecutables.Remove(nestedExecutable.Syntax);
        }
    }

    private static bool SelectorCallbackValueCanRunBetween(
        IOperation callbackValue,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        ISet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        executionPosition = -1;
        var alreadyGuardedExecutable = callbackValue is IAnonymousFunctionOperation;
        if (!alreadyGuardedExecutable && !visitedExecutables.Add(callbackValue.Syntax))
            return false;

        try
        {
            for (var current = callbackValue.Parent;
                 current != null && !ReferenceEquals(current, executableRoot);
                 current = current.Parent)
            {
                if (current is IInvocationOperation invocation &&
                    !SelectorCallbackInvocationCanExecuteValue(invocation, callbackValue))
                {
                    return false;
                }

                if (IsSelectorCallbackTrigger(current))
                {
                    return NestedExecutableTriggerCanRunBetween(
                        current,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }

                if (current is IVariableDeclaratorOperation declarator)
                {
                    var bindingPosition = GetSelectorCallbackBindingPosition(
                        declarator,
                        executableRoot,
                        afterPosition,
                        beforePosition);
                    return SelectorCallbackStorageCanRunBetween(
                        declarator.Symbol,
                        declarator,
                        callbackValue,
                        bindingPosition,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }

                if (current is ISimpleAssignmentOperation assignment &&
                    assignment.Value.Syntax.Span.Contains(callbackValue.Syntax.Span) &&
                    TryGetSelectorCallbackStorageSymbol(assignment.Target, out var storage))
                {
                    var bindingPosition = GetSelectorCallbackBindingPosition(
                        assignment,
                        executableRoot,
                        afterPosition,
                        beforePosition);
                    return SelectorCallbackStorageCanRunBetween(
                        storage,
                        assignment,
                        callbackValue,
                        bindingPosition,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }

                if (current is ICompoundAssignmentOperation
                    {
                        OperatorKind: BinaryOperatorKind.Subtract
                    } removal &&
                    removal.Target.Type?.TypeKind == TypeKind.Delegate &&
                    removal.Syntax.Span.Contains(callbackValue.Syntax.Span))
                {
                    return false;
                }

                if (current is ICompoundAssignmentOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add
                    } compoundAssignment &&
                    compoundAssignment.Target.Type?.TypeKind == TypeKind.Delegate &&
                    compoundAssignment.Value.Syntax.Span.Contains(callbackValue.Syntax.Span) &&
                    TryGetSelectorCallbackStorageSymbol(
                        compoundAssignment.Target,
                        out var combinedStorage))
                {
                    var bindingPosition = GetSelectorCallbackBindingPosition(
                        compoundAssignment,
                        executableRoot,
                        afterPosition,
                        beforePosition);
                    return SelectorCallbackStorageCanRunBetween(
                        combinedStorage,
                        compoundAssignment,
                        callbackValue,
                        bindingPosition,
                        executableRoot,
                        afterPosition,
                        beforePosition,
                        visitedExecutables,
                        out executionPosition);
                }
            }

            return false;
        }
        finally
        {
            if (!alreadyGuardedExecutable)
                visitedExecutables.Remove(callbackValue.Syntax);
        }
    }

    private static int GetSelectorCallbackBindingPosition(
        IOperation binding,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition)
    {
        return IsInsideNestedExecutable(binding, executableRoot) &&
               CanNestedExecutableOperationRunBetween(
                   binding,
                   executableRoot,
                   afterPosition,
                   beforePosition,
                   new HashSet<SyntaxNode>(),
                   out var executionPosition)
            ? executionPosition
            : binding.Syntax.SpanStart;
    }

    private static bool SelectorCallbackInvocationCanExecuteValue(
        IInvocationOperation invocation,
        IOperation callbackValue)
    {
        if (invocation.Instance?.Syntax.Span.Contains(callbackValue.Syntax.Span) == true)
        {
            if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke ||
                invocation.TargetMethod.Name is "DynamicInvoke" or "BeginInvoke")
            {
                return true;
            }

            var reducedMethod = invocation.TargetMethod.ReducedFrom;
            return reducedMethod != null &&
                   SourceCallableMayUseParameter(reducedMethod, reducedMethod.Parameters[0]);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter != null &&
                argument.Value.Syntax.Span.Contains(callbackValue.Syntax.Span))
            {
                return SourceCallableMayUseParameter(
                    invocation.TargetMethod,
                    argument.Parameter);
            }
        }

        return true;
    }

    private static bool SourceCallableMayUseParameter(
        IMethodSymbol method,
        IParameterSymbol parameter)
    {
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return true;

        return method.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax().DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText == parameter.Name));
    }

    private static bool SelectorCallbackStorageCanRunBetween(
        ISymbol storage,
        IOperation binding,
        IOperation boundCallbackValue,
        int assignmentPosition,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        ISet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        executionPosition = -1;
        foreach (var reference in executableRoot.Descendants())
        {
            if (!ReferencesSelectorCallbackStorage(reference, storage) ||
                IsSelectorCallbackStorageWrite(reference))
                continue;

            if (!SelectorCallbackValueCanRunBetween(
                    reference,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    visitedExecutables,
                    out var referenceExecutionPosition))
                continue;

            var bindingReadPosition = reference.Syntax.SpanStart;
            if (IsInsideNestedExecutable(reference, executableRoot))
            {
                var readAfterPosition = afterPosition > assignmentPosition
                    ? afterPosition
                    : assignmentPosition;
                if (TryGetDirectLocalFunctionInvocationPosition(
                        reference,
                        executableRoot,
                        readAfterPosition,
                        beforePosition,
                        out var directReadPosition) ||
                    CanNestedExecutableOperationRunBetween(
                        reference,
                        executableRoot,
                        readAfterPosition,
                        beforePosition,
                        new HashSet<SyntaxNode>(),
                        out directReadPosition))
                {
                    bindingReadPosition = directReadPosition;
                }
                else
                {
                    bindingReadPosition = referenceExecutionPosition;
                }
            }
            var bindingExecutable = FindNearestNestedExecutable(binding, executableRoot);
            var referenceExecutable = FindNearestNestedExecutable(reference, executableRoot);
            var sharesNestedExecution = bindingExecutable != null &&
                                        referenceExecutable != null &&
                                        bindingExecutable.Syntax == referenceExecutable.Syntax;
            var readsAfterBinding = bindingReadPosition > assignmentPosition ||
                                    bindingReadPosition == assignmentPosition &&
                                    sharesNestedExecution &&
                                    reference.Syntax.SpanStart > binding.Syntax.SpanStart;
            if (!readsAfterBinding ||
                storage is ILocalSymbol local &&
                (LocalDelegateIsOverwrittenBetween(
                     executableRoot,
                     local,
                     assignmentPosition,
                     bindingReadPosition) ||
                 sharesNestedExecution &&
                 LocalDelegateIsOverwrittenWithinNestedExecution(
                     bindingExecutable!,
                     binding,
                     reference,
                     local)) ||
                SelectorCallbackIdentityIsRemovedBetween(
                    storage,
                    boundCallbackValue,
                    binding,
                    reference,
                    executableRoot,
                    assignmentPosition,
                    bindingReadPosition,
                    bindingExecutable,
                    sharesNestedExecution))
            {
                continue;
            }

            executionPosition = referenceExecutionPosition;
            return true;
        }

        return false;
    }

    private static bool SelectorCallbackIdentityIsRemovedBetween(
        ISymbol storage,
        IOperation boundCallbackValue,
        IOperation binding,
        IOperation reference,
        IOperation executableRoot,
        int assignmentPosition,
        int bindingReadPosition,
        IOperation? bindingExecutable,
        bool sharesNestedExecution)
    {
        var identity = UnwrapSelectorCallbackIdentity(boundCallbackValue);
        var identityEvents = new List<(
            ICompoundAssignmentOperation Operation,
            int Position,
            int Multiplicity)>();
        foreach (var candidate in executableRoot.Descendants()
                     .OfType<ICompoundAssignmentOperation>())
        {
            if (candidate.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract) ||
                !TryGetSelectorCallbackStorageSymbol(candidate.Target, out var candidateStorage) ||
                !SymbolEqualityComparer.Default.Equals(candidateStorage, storage) ||
                !SelectorCallbackIdentitiesMatch(
                    boundCallbackValue,
                    candidate.Value,
                    executableRoot,
                    resolveRemovedIdentityAtInvocation:
                        candidate.OperatorKind == BinaryOperatorKind.Subtract))
            {
                continue;
            }

            var candidateRunsBetween = TryGetDefiniteExecutionPosition(
                candidate,
                executableRoot,
                assignmentPosition,
                bindingReadPosition,
                new HashSet<SyntaxNode>(),
                out var candidatePosition);
            if (!candidateRunsBetween &&
                candidate.OperatorKind == BinaryOperatorKind.Subtract)
            {
                candidateRunsBetween =
                    TryGetDefiniteAnonymousFunctionInvocationPosition(
                        candidate,
                        executableRoot,
                        assignmentPosition,
                        (sharesNestedExecution ||
                         ShareContainingAnonymousFunctionSyntax(
                             binding,
                             reference)) &&
                        reference.Syntax.SpanStart > bindingReadPosition
                            ? reference.Syntax.SpanStart
                            : bindingReadPosition,
                        out candidatePosition);
            }

            var candidateMayRunBetween = candidateRunsBetween;
            var candidateMayPosition = candidatePosition;
            if (!candidateMayRunBetween &&
                candidate.OperatorKind == BinaryOperatorKind.Add)
            {
                candidateMayRunBetween = IsInsideNestedExecutable(candidate, executableRoot)
                    ? CanNestedExecutableOperationRunBetween(
                        candidate,
                        executableRoot,
                        assignmentPosition,
                        bindingReadPosition,
                        new HashSet<SyntaxNode>(),
                        out candidateMayPosition)
                    : CanOperationRunBetween(
                        candidate,
                        executableRoot,
                        assignmentPosition,
                        bindingReadPosition,
                        useStableStorageReceiver: false);
                if (candidateMayRunBetween &&
                    !IsInsideNestedExecutable(candidate, executableRoot))
                {
                    candidateMayPosition = candidate.Syntax.SpanStart;
                }
            }

            var candidateExecutable = FindNearestNestedExecutable(candidate, executableRoot);
            var candidateWithinSharedNestedExecution = sharesNestedExecution &&
                                                        bindingExecutable != null &&
                                                        candidateExecutable != null &&
                                                        bindingExecutable.Syntax == candidateExecutable.Syntax &&
                                                        candidate.Syntax.SpanStart > binding.Syntax.SpanStart &&
                                                        candidate.Syntax.SpanStart < reference.Syntax.SpanStart;
            var candidateSharesNestedExecution = candidateWithinSharedNestedExecution &&
                                                  IsUnconditionallyExecutedWithin(
                                                      candidate.Syntax,
                                                      GetNestedExecutableBodySyntax(bindingExecutable!));
            var candidateCanAffectIdentity = candidate.OperatorKind == BinaryOperatorKind.Add
                ? candidateMayRunBetween || candidateWithinSharedNestedExecution
                : candidateRunsBetween || candidateSharesNestedExecution;
            if (!candidateCanAffectIdentity)
                continue;

            if (identity is ILocalReferenceOperation identityLocal)
            {
                var identityMutationPosition = candidateRunsBetween
                    ? candidatePosition
                    : candidateMayPosition;
                var identityChanged = candidate.OperatorKind == BinaryOperatorKind.Subtract
                    ? (candidateRunsBetween &&
                       LocalDelegateMayBeMutatedBetween(
                           executableRoot,
                           identityLocal.Local,
                           assignmentPosition,
                           identityMutationPosition) ||
                       candidateWithinSharedNestedExecution &&
                       LocalDelegateMayBeMutatedWithinNestedExecution(
                           bindingExecutable!,
                           binding,
                           candidate,
                           identityLocal.Local))
                    : ((candidateRunsBetween || candidateMayRunBetween) &&
                       LocalDelegateIsMutatedBetween(
                           executableRoot,
                           identityLocal.Local,
                           assignmentPosition,
                           identityMutationPosition) ||
                       candidateWithinSharedNestedExecution &&
                       LocalDelegateIsMutatedWithinNestedExecution(
                           bindingExecutable!,
                           binding,
                           candidate,
                           identityLocal.Local));
                if (identityChanged)
                    continue;
            }

            var candidateMultiplicity = candidate.OperatorKind == BinaryOperatorKind.Add
                ? GetPossibleCallbackAdditionExecutionCount(candidate)
                : 1;
            var mappedEventPosition = candidateWithinSharedNestedExecution
                ? assignmentPosition
                : candidateRunsBetween
                    ? candidatePosition
                    : candidateMayPosition;
            var candidateEvents = new Dictionary<int, int>();
            if (candidateExecutable is not ILocalFunctionOperation ||
                candidateWithinSharedNestedExecution)
            {
                candidateEvents[mappedEventPosition] = candidateMultiplicity;
            }
            foreach (var directInvocation in
                     GetLocalFunctionInvocationPositions(
                         candidate,
                         executableRoot,
                         assignmentPosition,
                         bindingReadPosition,
                         requireDefinite: candidate.OperatorKind == BinaryOperatorKind.Subtract))
            {
                if (identity is ILocalReferenceOperation directIdentityLocal)
                {
                    var identityChangedBeforeInvocation =
                        candidate.OperatorKind == BinaryOperatorKind.Subtract
                            ? LocalDelegateMayBeMutatedBetween(
                                executableRoot,
                                directIdentityLocal.Local,
                                assignmentPosition,
                                directInvocation.Position)
                            : LocalDelegateIsMutatedBetween(
                                executableRoot,
                                directIdentityLocal.Local,
                                assignmentPosition,
                                directInvocation.Position);
                    if (identityChangedBeforeInvocation)
                        continue;
                }

                if (directInvocation.Multiplicity == 0)
                    continue;

                var directMultiplicity = candidate.OperatorKind == BinaryOperatorKind.Add
                    ? candidateMultiplicity > int.MaxValue / directInvocation.Multiplicity
                        ? int.MaxValue
                        : candidateMultiplicity * directInvocation.Multiplicity
                    : 1;
                if (!candidateEvents.TryGetValue(
                        directInvocation.Position,
                        out var existingMultiplicity) ||
                    directMultiplicity > existingMultiplicity)
                {
                    candidateEvents[directInvocation.Position] = directMultiplicity;
                }
            }

            foreach (var identityEvent in candidateEvents)
                identityEvents.Add((candidate, identityEvent.Key, identityEvent.Value));
        }

        var identityCount = GetPossibleCallbackBindingExecutionCount(
            binding,
            executableRoot,
            assignmentPosition);
        foreach (var identityEvent in identityEvents
                     .OrderBy(item => item.Position)
                     .ThenBy(item => item.Operation.Syntax.SpanStart))
        {
            if (identityEvent.Operation.OperatorKind == BinaryOperatorKind.Add)
            {
                identityCount = identityCount > int.MaxValue - identityEvent.Multiplicity
                    ? int.MaxValue
                    : identityCount + identityEvent.Multiplicity;
                continue;
            }

            if (identityCount > 0)
                identityCount--;
        }

        return identityCount == 0;
    }

    private static int GetPossibleCallbackBindingExecutionCount(
        IOperation binding,
        IOperation executableRoot,
        int assignmentPosition)
    {
        var bindingCount = GetPossibleCallbackAdditionExecutionCount(binding);
        ILocalFunctionOperation? localFunction = null;
        for (var parent = binding.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is ILocalFunctionOperation candidate)
            {
                localFunction = candidate;
                break;
            }

            if (parent is IAnonymousFunctionOperation)
                break;
        }

        if (localFunction == null)
            return bindingCount;

        var invocationCount = GetLocalFunctionInvocationOccurrences(
                localFunction,
                executableRoot,
                invocation => invocation.Syntax.Span.Contains(assignmentPosition),
                requireDefinite: false,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))
            .Values
            .DefaultIfEmpty(1)
            .Aggregate(SaturatingAdd);
        if (invocationCount == 0)
            return 0;

        return bindingCount > int.MaxValue / invocationCount
            ? int.MaxValue
            : bindingCount * invocationCount;
    }

    private static int GetPossibleCallbackAdditionExecutionCount(IOperation operation)
    {
        var executionCount = 1;
        foreach (var forStatement in operation.Syntax.Ancestors().OfType<ForStatementSyntax>())
        {
            if (!forStatement.Statement.Span.Contains(operation.Syntax.Span))
                continue;

            if (!TryGetSimpleForLoopIterationCount(forStatement, out var loopCount))
                return int.MaxValue;

            if (loopCount == 0)
                return 0;

            if (OperationIsImmediatelyFollowedByLoopBreak(operation, forStatement))
                loopCount = 1;

            executionCount = executionCount > int.MaxValue / loopCount
                ? int.MaxValue
                : executionCount * loopCount;
        }

        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is IForEachLoopOperation forEach &&
                forEach.Body.Syntax.Span.Contains(operation.Syntax.Span))
            {
                if (forEach.Collection.UnwrapConversions() is not IArrayCreationOperation
                    {
                        Initializer: { } initializer
                    })
                {
                    return int.MaxValue;
                }

                var loopCount = initializer.ElementValues.Length;
                if (loopCount == 0)
                    return 0;

                executionCount = executionCount > int.MaxValue / loopCount
                    ? int.MaxValue
                    : executionCount * loopCount;
                continue;
            }

            if (parent is IWhileLoopOperation whileLoop &&
                whileLoop.Body.Syntax.Span.Contains(operation.Syntax.Span))
            {
                return int.MaxValue;
            }
        }

        return executionCount;
    }

    private static bool OperationIsImmediatelyFollowedByLoopBreak(
        IOperation operation,
        ForStatementSyntax forStatement)
    {
        if (forStatement.Statement is not BlockSyntax block)
            return false;

        var containingStatement = block.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(operation.Syntax.Span));
        if (containingStatement is not ExpressionStatementSyntax)
            return false;

        var statementIndex = block.Statements.IndexOf(containingStatement);
        return statementIndex >= 0 &&
               statementIndex + 1 < block.Statements.Count &&
               block.Statements[statementIndex + 1] is BreakStatementSyntax;
    }

    private static bool TryGetSimpleForLoopIterationCount(
        ForStatementSyntax forStatement,
        out int iterationCount)
    {
        iterationCount = 0;
        if (forStatement.Declaration is not
            {
                Variables.Count: 1
            } declaration ||
            declaration.Variables[0] is not
            {
                Initializer.Value: { } initializer
            } variable ||
            !TryGetInt64Literal(initializer, out var start) ||
            forStatement.Condition is not BinaryExpressionSyntax condition ||
            condition.Left is not IdentifierNameSyntax conditionLocal ||
            conditionLocal.Identifier.ValueText != variable.Identifier.ValueText ||
            !TryGetInt64Literal(condition.Right, out var limit) ||
            forStatement.Incrementors.Count != 1)
        {
            return false;
        }

        var incrementor = forStatement.Incrementors[0];
        var increments = incrementor is PostfixUnaryExpressionSyntax postfixIncrement &&
                         postfixIncrement.IsKind(SyntaxKind.PostIncrementExpression) &&
                         postfixIncrement.Operand is IdentifierNameSyntax postfixIncrementLocal &&
                         postfixIncrementLocal.Identifier.ValueText == variable.Identifier.ValueText ||
                         incrementor is PrefixUnaryExpressionSyntax prefixIncrement &&
                         prefixIncrement.IsKind(SyntaxKind.PreIncrementExpression) &&
                         prefixIncrement.Operand is IdentifierNameSyntax prefixIncrementLocal &&
                         prefixIncrementLocal.Identifier.ValueText == variable.Identifier.ValueText;
        var decrements = incrementor is PostfixUnaryExpressionSyntax postfixDecrement &&
                         postfixDecrement.IsKind(SyntaxKind.PostDecrementExpression) &&
                         postfixDecrement.Operand is IdentifierNameSyntax postfixDecrementLocal &&
                         postfixDecrementLocal.Identifier.ValueText == variable.Identifier.ValueText ||
                         incrementor is PrefixUnaryExpressionSyntax prefixDecrement &&
                         prefixDecrement.IsKind(SyntaxKind.PreDecrementExpression) &&
                         prefixDecrement.Operand is IdentifierNameSyntax prefixDecrementLocal &&
                         prefixDecrementLocal.Identifier.ValueText == variable.Identifier.ValueText;

        decimal count;
        if (increments && condition.IsKind(SyntaxKind.LessThanExpression))
            count = limit > start ? (decimal)limit - start : 0;
        else if (increments && condition.IsKind(SyntaxKind.LessThanOrEqualExpression))
            count = limit >= start ? (decimal)limit - start + 1 : 0;
        else if (decrements && condition.IsKind(SyntaxKind.GreaterThanExpression))
            count = start > limit ? (decimal)start - limit : 0;
        else if (decrements && condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
            count = start >= limit ? (decimal)start - limit + 1 : 0;
        else
            return false;

        iterationCount = count >= int.MaxValue ? int.MaxValue : (int)count;
        return true;
    }

    private static bool TryGetInt64Literal(ExpressionSyntax expression, out long value)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        if (expression is not LiteralExpressionSyntax literal)
        {
            value = 0;
            return false;
        }

        switch (literal.Token.Value)
        {
            case sbyte signedByte:
                value = signedByte;
                return true;
            case byte unsignedByte:
                value = unsignedByte;
                return true;
            case short signedShort:
                value = signedShort;
                return true;
            case ushort unsignedShort:
                value = unsignedShort;
                return true;
            case int signedInt:
                value = signedInt;
                return true;
            case uint unsignedInt:
                value = unsignedInt;
                return true;
            case long signedLong:
                value = signedLong;
                return true;
            case ulong unsignedLong when unsignedLong <= long.MaxValue:
                value = (long)unsignedLong;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool SelectorCallbackIdentitiesMatch(
        IOperation boundCallbackValue,
        IOperation removedCallbackValue,
        IOperation executableRoot,
        bool resolveRemovedIdentityAtInvocation)
    {
        var boundIdentity = ResolveStableSelectorCallbackIdentity(
            boundCallbackValue,
            executableRoot);
        var removedReferencePosition = resolveRemovedIdentityAtInvocation &&
                                       TryGetAnonymousFunctionInvocationPosition(
                                           removedCallbackValue,
                                           executableRoot,
                                           out var invocationPosition)
            ? invocationPosition
            : (int?)null;
        var removedIdentity = ResolveStableSelectorCallbackIdentity(
            removedCallbackValue,
            executableRoot,
            removedReferencePosition);
        if (boundIdentity is ILocalReferenceOperation boundLocal &&
            removedIdentity is ILocalReferenceOperation removedLocal)
        {
            return SymbolEqualityComparer.Default.Equals(boundLocal.Local, removedLocal.Local);
        }

        if (boundIdentity is IMethodReferenceOperation boundMethod &&
            removedIdentity is IMethodReferenceOperation removedMethod &&
            SymbolEqualityComparer.Default.Equals(
                boundMethod.Method.OriginalDefinition,
                removedMethod.Method.OriginalDefinition))
        {
            return boundMethod.Instance == null && removedMethod.Instance == null ||
                   boundMethod.Instance != null &&
                   removedMethod.Instance != null &&
                   boundMethod.Instance.Syntax.ToString() == removedMethod.Instance.Syntax.ToString();
        }

        return false;
    }

    private static IOperation ResolveStableSelectorCallbackIdentity(
        IOperation value,
        IOperation executableRoot,
        int? effectiveReferencePosition = null)
    {
        var identity = UnwrapSelectorCallbackIdentity(value);
        var referencePosition = effectiveReferencePosition ??
                                GetEffectiveSelectorCallbackReferencePosition(
                                    identity,
                                    executableRoot);
        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        while (identity is ILocalReferenceOperation localReference &&
               visitedLocals.Add(localReference.Local))
        {
            var declarator = executableRoot.Descendants()
                .OfType<IVariableDeclaratorOperation>()
                .FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Symbol,
                        localReference.Local));
            if (declarator?.Initializer?.Value is not { } initializer ||
                declarator.Syntax.SpanStart >= referencePosition ||
                LocalDelegateMayBeMutatedBetween(
                    executableRoot,
                    localReference.Local,
                    declarator.Syntax.Span.End,
                    referencePosition))
            {
                break;
            }

            referencePosition = initializer.Syntax.SpanStart;
            identity = UnwrapSelectorCallbackIdentity(initializer);
        }

        return identity;
    }

    private static int GetEffectiveSelectorCallbackReferencePosition(
        IOperation identity,
        IOperation executableRoot)
    {
        var nestedExecutable = FindNearestNestedExecutable(identity, executableRoot);
        if (nestedExecutable is IAnonymousFunctionOperation anonymousFunction)
        {
            if (!TryGetAnonymousFunctionStorageSymbol(anonymousFunction, out var storage))
                return identity.Syntax.SpanStart;

            return executableRoot.Descendants()
                .OfType<IInvocationOperation>()
                .Where(invocation => IsInvocationOfSelectorCallbackStorage(invocation, storage))
                .Select(invocation => invocation.Syntax.SpanStart)
                .DefaultIfEmpty(identity.Syntax.SpanStart)
                .Max();
        }

        if (nestedExecutable is not ILocalFunctionOperation localFunction)
        {
            return identity.Syntax.SpanStart;
        }

        return GetLocalFunctionInvocationOccurrences(
                localFunction,
                executableRoot,
                _ => true,
                requireDefinite: false,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))
            .Keys
            .DefaultIfEmpty(identity.Syntax.SpanStart)
            .Max();
    }

    private static bool TryGetAnonymousFunctionInvocationPosition(
        IOperation operation,
        IOperation executableRoot,
        out int invocationPosition)
    {
        if (!TryGetContainingAnonymousFunctionStorage(
                operation,
                executableRoot,
                out var anonymousFunction,
                out var storage))
        {
            invocationPosition = -1;
            return false;
        }

        invocationPosition = executableRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Where(invocation =>
                InvocationDefinitelyExecutesSelectorCallbackStorage(
                    invocation,
                    storage,
                    executableRoot) &&
                (storage is not ILocalSymbol local ||
                 !LocalDelegateIsOverwrittenBetween(
                     executableRoot,
                     local,
                     anonymousFunction.SpanStart,
                     invocation.Syntax.SpanStart)))
            .Select(invocation => invocation.Syntax.SpanStart)
            .DefaultIfEmpty(-1)
            .Max();
        return invocationPosition >= 0;
    }

    private static bool TryGetDefiniteAnonymousFunctionInvocationPosition(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        out int invocationPosition)
    {
        if (!TryGetContainingAnonymousFunctionStorage(
                operation,
                executableRoot,
                out var anonymousFunction,
                out var storage))
        {
            invocationPosition = -1;
            return false;
        }

        var containingAnonymousFunction = anonymousFunction.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        invocationPosition = executableRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Where(invocation =>
                InvocationDefinitelyExecutesSelectorCallbackStorage(
                    invocation,
                    storage,
                    executableRoot) &&
                (storage is not ILocalSymbol local ||
                 !LocalDelegateIsOverwrittenBetween(
                     executableRoot,
                     local,
                     anonymousFunction.SpanStart,
                     invocation.Syntax.SpanStart)) &&
                (IsOperationDefinitelyExecutedBetween(
                     invocation,
                     executableRoot,
                     afterPosition,
                     beforePosition) ||
                 containingAnonymousFunction != null &&
                 invocation.Syntax.SpanStart > afterPosition &&
                 invocation.Syntax.SpanStart < beforePosition &&
                 invocation.Syntax.Ancestors()
                     .OfType<AnonymousFunctionExpressionSyntax>()
                     .FirstOrDefault() == containingAnonymousFunction &&
                 IsUnconditionallyExecutedWithin(
                     invocation.Syntax,
                     containingAnonymousFunction.Body)))
            .Select(invocation => invocation.Syntax.SpanStart)
            .DefaultIfEmpty(-1)
            .Max();
        return invocationPosition >= 0;
    }

    private static bool TryGetContainingAnonymousFunctionStorage(
        IOperation operation,
        IOperation executableRoot,
        out AnonymousFunctionExpressionSyntax anonymousFunctionSyntax,
        out ISymbol storage)
    {
        var containingAnonymousFunctionSyntax = operation.Syntax.AncestorsAndSelf()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault()!;
        if (containingAnonymousFunctionSyntax == null)
        {
            anonymousFunctionSyntax = null!;
            storage = null!;
            return false;
        }

        var anonymousFunction = executableRoot.Descendants()
            .OfType<IAnonymousFunctionOperation>()
            .FirstOrDefault(candidate => candidate.Syntax == containingAnonymousFunctionSyntax);
        if (anonymousFunction != null &&
            TryGetAnonymousFunctionStorageSymbol(anonymousFunction, out storage))
        {
            anonymousFunctionSyntax = containingAnonymousFunctionSyntax;
            return true;
        }

        anonymousFunctionSyntax = null!;
        storage = null!;
        return false;
    }

    private static bool IsInvocationOfSelectorCallbackStorage(
        IInvocationOperation invocation,
        ISymbol storage)
    {
        return invocation.Instance != null &&
               TryGetSelectorCallbackStorageSymbol(invocation.Instance, out var invocationStorage) &&
               SymbolEqualityComparer.Default.Equals(invocationStorage, storage);
    }

    private static bool InvocationDefinitelyExecutesSelectorCallbackStorage(
        IInvocationOperation invocation,
        ISymbol storage,
        IOperation executableRoot)
    {
        if (IsInvocationOfSelectorCallbackStorage(invocation, storage))
            return true;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter != null &&
                TryGetSelectorCallbackStorageSymbol(argument.Value, out var argumentStorage) &&
                SymbolEqualityComparer.Default.Equals(argumentStorage, storage) &&
                SourceCallableDefinitelyInvokesParameter(
                    invocation.TargetMethod,
                    argument.Parameter,
                    executableRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SourceCallableDefinitelyInvokesParameter(
        IMethodSymbol method,
        IParameterSymbol parameter,
        IOperation executableRoot)
    {
        if (method.IsAsync || method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return false;

        var rootSemanticModel = executableRoot.SemanticModel;
        if (rootSemanticModel == null)
            return false;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            if (declaration.DescendantNodes().Any(node => node is YieldStatementSyntax) ||
                !TryGetSingleCallableBodyExpression(declaration, out var expression))
            {
                continue;
            }

            var semanticModel = rootSemanticModel.SyntaxTree == expression.SyntaxTree
                ? rootSemanticModel
                : rootSemanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
            if (semanticModel.GetOperation(expression) is IInvocationOperation callbackInvocation &&
                callbackInvocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                callbackInvocation.Instance?.UnwrapConversions() is
                    IParameterReferenceOperation parameterReference &&
                parameterReference.Parameter.Ordinal == parameter.Ordinal)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSingleCallableBodyExpression(
        SyntaxNode declaration,
        out ExpressionSyntax expression)
    {
        if (declaration is MethodDeclarationSyntax method)
        {
            if (method.ExpressionBody?.Expression is { } expressionBody)
            {
                expression = expressionBody;
                return true;
            }

            if (method.Body?.Statements.Count == 1 &&
                method.Body.Statements[0] is ExpressionStatementSyntax expressionStatement)
            {
                expression = expressionStatement.Expression;
                return true;
            }
        }

        if (declaration is LocalFunctionStatementSyntax localFunction)
        {
            if (localFunction.ExpressionBody?.Expression is { } expressionBody)
            {
                expression = expressionBody;
                return true;
            }

            if (localFunction.Body?.Statements.Count == 1 &&
                localFunction.Body.Statements[0] is ExpressionStatementSyntax expressionStatement)
            {
                expression = expressionStatement.Expression;
                return true;
            }
        }

        expression = null!;
        return false;
    }

    private static bool ShareContainingAnonymousFunctionSyntax(
        IOperation left,
        IOperation right)
    {
        var leftAnonymousFunction = left.Syntax.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        return leftAnonymousFunction != null &&
               leftAnonymousFunction == right.Syntax.Ancestors()
                   .OfType<AnonymousFunctionExpressionSyntax>()
                   .FirstOrDefault();
    }

    private static bool TryGetAnonymousFunctionStorageSymbol(
        IAnonymousFunctionOperation anonymousFunction,
        out ISymbol storage)
    {
        for (IOperation? current = anonymousFunction;
             current?.Parent != null;
             current = current.Parent)
        {
            if (current.Parent is IVariableDeclaratorOperation declarator &&
                declarator.Initializer?.Value.Syntax.Span.Contains(
                    anonymousFunction.Syntax.Span) == true)
            {
                storage = declarator.Symbol;
                return true;
            }

            if (current.Parent is ISimpleAssignmentOperation assignment &&
                assignment.Value.Syntax.Span.Contains(anonymousFunction.Syntax.Span) &&
                TryGetSelectorCallbackStorageSymbol(assignment.Target, out storage))
            {
                return true;
            }
        }

        storage = null!;
        return false;
    }

    private static IOperation UnwrapSelectorCallbackIdentity(IOperation value)
    {
        while (true)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    value = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    value = parenthesized.Operand;
                    continue;
                case IDelegateCreationOperation delegateCreation:
                    value = delegateCreation.Target;
                    continue;
                default:
                    return value;
            }
        }
    }

    private static bool IsSelectorCallbackStorageWrite(IOperation reference)
    {
        for (var current = reference;
             current.Parent is IConversionOperation or IParenthesizedOperation;
             current = current.Parent)
        {
            reference = current.Parent;
        }

        return reference.Parent is ISimpleAssignmentOperation assignment &&
               assignment.Target.Syntax.Span.Contains(reference.Syntax.Span);
    }

    private static IOperation? FindNearestNestedExecutable(
        IOperation operation,
        IOperation root)
    {
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, root);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return parent;
        }

        return null;
    }

    private static SyntaxNode GetNestedExecutableBodySyntax(IOperation nestedExecutable)
    {
        return nestedExecutable switch
        {
            ILocalFunctionOperation localFunction =>
                localFunction.Body?.Syntax ?? localFunction.Syntax,
            IAnonymousFunctionOperation anonymousFunction => anonymousFunction.Body.Syntax,
            _ => nestedExecutable.Syntax
        };
    }

    private static bool LocalDelegateIsMutatedBetween(
        IOperation executableRoot,
        ILocalSymbol local,
        int assignmentPosition,
        int invocationPosition)
    {
        return executableRoot.Descendants().Any(operation =>
            IsOperationDefinitelyExecutedBetween(
                operation,
                executableRoot,
                assignmentPosition,
                invocationPosition) &&
            (operation is IAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static bool LocalDelegateMayBeMutatedBetween(
        IOperation executableRoot,
        ILocalSymbol local,
        int assignmentPosition,
        int invocationPosition)
    {
        return executableRoot.Descendants().Any(operation =>
            CanOperationOrNestedExecutableRunBetween(
                operation,
                executableRoot,
                assignmentPosition,
                invocationPosition) &&
            (operation is IAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static bool LocalDelegateIsMutatedWithinNestedExecution(
        IOperation nestedExecutable,
        IOperation binding,
        IOperation reference,
        ILocalSymbol local)
    {
        return nestedExecutable.Descendants().Any(operation =>
            operation.Syntax.SpanStart > binding.Syntax.SpanStart &&
            operation.Syntax.SpanStart < reference.Syntax.SpanStart &&
            !IsInsideNestedExecutable(operation, nestedExecutable) &&
            IsUnconditionallyExecutedWithin(
                operation.Syntax,
                GetNestedExecutableBodySyntax(nestedExecutable)) &&
            (operation is IAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static bool LocalDelegateMayBeMutatedWithinNestedExecution(
        IOperation nestedExecutable,
        IOperation binding,
        IOperation reference,
        ILocalSymbol local)
    {
        return nestedExecutable.Descendants().Any(operation =>
            operation.Syntax.SpanStart > binding.Syntax.SpanStart &&
            operation.Syntax.SpanStart < reference.Syntax.SpanStart &&
            !IsInsideNestedExecutable(operation, nestedExecutable) &&
            (operation is IAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static bool LocalDelegateIsOverwrittenWithinNestedExecution(
        IOperation nestedExecutable,
        IOperation binding,
        IOperation reference,
        ILocalSymbol local)
    {
        if (reference.Syntax.SpanStart <= binding.Syntax.SpanStart)
            return false;

        return nestedExecutable.Descendants().Any(operation =>
            operation.Syntax.SpanStart > binding.Syntax.SpanStart &&
            operation.Syntax.SpanStart < reference.Syntax.SpanStart &&
            !IsInsideNestedExecutable(operation, nestedExecutable) &&
            IsUnconditionallyExecutedWithin(
                operation.Syntax,
                GetNestedExecutableBodySyntax(nestedExecutable)) &&
            (operation is ISimpleAssignmentOperation assignment &&
             assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
             SymbolEqualityComparer.Default.Equals(localReference.Local, local) ||
             operation is IArgumentOperation argument &&
             argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
             argument.Value.UnwrapConversions() is ILocalReferenceOperation argumentLocal &&
             SymbolEqualityComparer.Default.Equals(argumentLocal.Local, local)));
    }

    private static IEnumerable<(int Position, int Multiplicity)>
        GetLocalFunctionInvocationPositions(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        bool requireDefinite)
    {
        ILocalFunctionOperation? localFunction = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is ILocalFunctionOperation candidate)
            {
                localFunction = candidate;
                break;
            }

            if (parent is IAnonymousFunctionOperation)
                break;
        }

        if (localFunction == null)
            yield break;

        var occurrences = GetLocalFunctionInvocationOccurrences(
            localFunction,
            executableRoot,
            invocation => requireDefinite
                ? IsOperationDefinitelyExecutedBetween(
                    invocation,
                    executableRoot,
                    afterPosition,
                    beforePosition)
                : CanOperationRunBetween(
                    invocation,
                    executableRoot,
                    afterPosition,
                    beforePosition,
                    useStableStorageReceiver: false),
            requireDefinite,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        foreach (var occurrence in occurrences)
            yield return (occurrence.Key, occurrence.Value);
    }

    private static Dictionary<int, int> GetLocalFunctionInvocationOccurrences(
        ILocalFunctionOperation localFunction,
        IOperation executableRoot,
        Func<IInvocationOperation, bool> rootInvocationMatches,
        bool requireDefinite,
        ISet<IMethodSymbol> activeLocalFunctions)
    {
        var occurrences = new Dictionary<int, int>();
        var localFunctionSymbol = localFunction.Symbol.OriginalDefinition;
        if (!activeLocalFunctions.Add(localFunctionSymbol))
            return occurrences;

        foreach (var invocation in executableRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    localFunctionSymbol))
            {
                continue;
            }

            var containingExecutable = FindNearestNestedExecutable(invocation, executableRoot);
            if (containingExecutable is IAnonymousFunctionOperation)
                continue;

            if (containingExecutable is ILocalFunctionOperation selfContainingLocalFunction &&
                SymbolEqualityComparer.Default.Equals(
                    selfContainingLocalFunction.Symbol.OriginalDefinition,
                    localFunctionSymbol))
            {
                continue;
            }

            var invocationMultiplicity = requireDefinite
                ? 1
                : GetPossibleCallbackAdditionExecutionCount(invocation);
            if (invocationMultiplicity == 0)
                continue;

            if (containingExecutable is ILocalFunctionOperation containingLocalFunction)
            {
                if (requireDefinite &&
                    !IsUnconditionallyExecutedWithin(
                        invocation.Syntax,
                        GetNestedExecutableBodySyntax(containingLocalFunction)))
                {
                    continue;
                }

                var outerOccurrences = GetLocalFunctionInvocationOccurrences(
                    containingLocalFunction,
                    executableRoot,
                    rootInvocationMatches,
                    requireDefinite,
                    activeLocalFunctions);
                foreach (var outerOccurrence in outerOccurrences)
                {
                    AddInvocationOccurrence(
                        occurrences,
                        outerOccurrence.Key,
                        requireDefinite
                            ? 1
                            : SaturatingMultiply(
                                invocationMultiplicity,
                                outerOccurrence.Value));
                }

                continue;
            }

            if (rootInvocationMatches(invocation))
                AddInvocationOccurrence(
                    occurrences,
                    invocation.Syntax.SpanStart,
                    invocationMultiplicity);
        }

        activeLocalFunctions.Remove(localFunctionSymbol);
        return occurrences;
    }

    private static void AddInvocationOccurrence(
        IDictionary<int, int> occurrences,
        int position,
        int multiplicity)
    {
        occurrences[position] = occurrences.TryGetValue(position, out var existingMultiplicity)
            ? SaturatingAdd(existingMultiplicity, multiplicity)
            : multiplicity;
    }

    private static int SaturatingAdd(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;

    private static int SaturatingMultiply(int left, int right) =>
        left > int.MaxValue / right ? int.MaxValue : left * right;

    private static bool TryGetDirectLocalFunctionInvocationPosition(
        IOperation operation,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        out int invocationPosition)
    {
        ILocalFunctionOperation? localFunction = null;
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, executableRoot);
             parent = parent.Parent)
        {
            if (parent is ILocalFunctionOperation candidate)
            {
                localFunction = candidate;
                break;
            }

            if (parent is IAnonymousFunctionOperation)
                break;
        }

        invocationPosition = executableRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Where(invocation =>
                localFunction != null &&
                !IsInsideNestedExecutable(invocation, executableRoot) &&
                invocation.Syntax.SpanStart > afterPosition &&
                invocation.Syntax.SpanStart < beforePosition &&
                SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    localFunction.Symbol.OriginalDefinition))
            .Select(invocation => invocation.Syntax.SpanStart)
            .DefaultIfEmpty(-1)
            .Min();
        return invocationPosition >= 0;
    }

    private static bool NestedExecutableTriggerCanRunBetween(
        IOperation trigger,
        IOperation executableRoot,
        int afterPosition,
        int beforePosition,
        ISet<SyntaxNode> visitedExecutables,
        out int executionPosition)
    {
        if (!IsInsideNestedExecutable(trigger, executableRoot))
        {
            if (trigger.Syntax.SpanStart > afterPosition &&
                trigger.Syntax.SpanStart < beforePosition)
            {
                executionPosition = trigger.Syntax.SpanStart;
                return true;
            }

            if (SelectorCallbackTriggerCrossesPosition(
                    trigger,
                    afterPosition,
                    beforePosition))
            {
                executionPosition = trigger.Syntax.Span.End;
                return true;
            }

            executionPosition = -1;
            return false;
        }

        return CanNestedExecutableOperationRunBetween(
            trigger,
            executableRoot,
            afterPosition,
            beforePosition,
            visitedExecutables,
            out executionPosition);
    }

    private static bool SelectorCallbackTriggerCrossesPosition(
        IOperation trigger,
        int afterPosition,
        int beforePosition)
    {
        for (IOperation? current = trigger; current != null; current = current.Parent)
        {
            if (IsSelectorCallbackTrigger(current) &&
                (current.Syntax.SpanStart > afterPosition &&
                 current.Syntax.SpanStart < beforePosition ||
                 current.Syntax.Span.Contains(afterPosition) &&
                 current.Syntax.Span.End < beforePosition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelectorCallbackTrigger(IOperation operation)
    {
        if (operation is IAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is IPropertyReferenceOperation)
        {
            return true;
        }

        return operation is IInvocationOperation or
            IDynamicInvocationOperation or
            IObjectCreationOperation or
            IDynamicObjectCreationOperation or
            IEventAssignmentOperation or
            IPropertyReferenceOperation { Property.IsIndexer: true };
    }

    private static bool TryGetSelectorCallbackStorageSymbol(
        IOperation target,
        out ISymbol storage)
    {
        target = target.UnwrapConversions();
        switch (target)
        {
            case ILocalReferenceOperation localReference:
                storage = localReference.Local;
                return true;

            case IFieldReferenceOperation fieldReference:
                storage = fieldReference.Field;
                return true;

            case IPropertyReferenceOperation propertyReference:
                storage = propertyReference.Property;
                return true;

            default:
                storage = null!;
                return false;
        }
    }

    private static bool ReferencesSelectorCallbackStorage(
        IOperation operation,
        ISymbol storage)
    {
        return operation is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, storage) ||
               operation is IFieldReferenceOperation fieldReference &&
               SymbolEqualityComparer.Default.Equals(fieldReference.Field, storage) ||
               operation is IPropertyReferenceOperation propertyReference &&
               SymbolEqualityComparer.Default.Equals(propertyReference.Property, storage);
    }

    private static bool TryGetStableLocalReference(
        IOperation value,
        out ILocalSymbol local)
    {
        while (true)
        {
            switch (value)
            {
                case IParenthesizedOperation parenthesized:
                    value = parenthesized.Operand;
                    continue;

                case IConversionOperation { OperatorMethod: null } conversion:
                    value = conversion.Operand;
                    continue;

                case ILocalReferenceOperation localReference:
                    local = localReference.Local;
                    return true;

                default:
                    local = null!;
                    return false;
            }
        }
    }

    private static bool IsEnumerableSelect(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Select" &&
               invocation.TargetMethod.ContainingType.Name == "Enumerable" &&
               invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Linq" &&
               invocation.Arguments.Length >= 2;
    }

    private static bool TryGetArgumentValue(
        IInvocationOperation invocation,
        int parameterOrdinal,
        out IOperation value)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == parameterOrdinal)
            {
                value = argument.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static bool TryGetAnonymousFunction(
        IOperation operation,
        out IAnonymousFunctionOperation selector)
    {
        operation = operation.UnwrapConversions();
        if (operation is IDelegateCreationOperation delegateCreation)
            operation = delegateCreation.Target.UnwrapConversions();

        if (operation is IAnonymousFunctionOperation anonymousFunction)
        {
            selector = anonymousFunction;
            return true;
        }

        selector = null!;
        return false;
    }

    private static bool SourceIsAtMostOne(
        IOperation source,
        IOperation executableRoot,
        int beforePosition,
        System.Threading.CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        if (!TryUnwrapCardinalityPreservingOperations(source, out source))
            return false;

        if (source is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            TryGetSingleCardinalityAssignmentBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                cancellationToken,
                out var assignedValue) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            !EnumerateOutsideNestedExecutables(executableRoot).Any(operation =>
                operation.Syntax.SpanStart < beforePosition &&
                operation is IDynamicInvocationOperation dynamicInvocation &&
                dynamicInvocation.ReferencesLocal(localReference.Local)))
        {
            return SourceIsAtMostOne(
                assignedValue,
                executableRoot,
                beforePosition,
                cancellationToken,
                visitedLocals);
        }

        if (source is IArrayCreationOperation arrayCreation)
        {
            if (arrayCreation.Initializer != null)
                return arrayCreation.Initializer.ElementValues.Length <= 1;

            return arrayCreation.DimensionSizes.Length == 1 &&
                   IsConstantArrayLengthAtMostOne(arrayCreation.DimensionSizes[0]);
        }

        if (source is IInvocationOperation invocation &&
            invocation.TargetMethod.ContainingType.Name == "Enumerable" &&
            invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Linq")
        {
            if (invocation.TargetMethod.Name == "Empty" &&
                invocation.Arguments.Length == 0)
            {
                return true;
            }

            if ((invocation.TargetMethod.Name is "Repeat" or "Take") &&
                invocation.Arguments.Length >= 2 &&
                TryGetArgumentValue(invocation, 1, out var countValue) &&
                countValue.ConstantValue is { HasValue: true, Value: int count })
            {
                return count <= 1;
            }
        }

        return false;
    }

    private static bool TryGetSingleCardinalityAssignmentBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        int beforePosition,
        System.Threading.CancellationToken cancellationToken,
        out IOperation value)
    {
        var assignments = LocalAssignmentCache.GetAssignments(
                executableRoot,
                local,
                cancellationToken)
            .Where(assignment => assignment.SpanStart < beforePosition)
            .ToArray();
        if (assignments.Length == 1)
        {
            value = assignments[0].Value;
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryUnwrapCardinalityPreservingOperations(
        IOperation source,
        out IOperation unwrapped)
    {
        while (true)
        {
            if (source is IParenthesizedOperation parenthesized)
            {
                source = parenthesized.Operand;
                continue;
            }

            if (source is IConversionOperation conversion)
            {
                if (conversion.OperatorMethod != null ||
                    conversion.Operand.Type?.TypeKind == TypeKind.Dynamic ||
                    conversion.Type?.TypeKind == TypeKind.Dynamic)
                {
                    unwrapped = source;
                    return false;
                }

                source = conversion.Operand;
                continue;
            }

            unwrapped = source;
            return true;
        }
    }

    private static bool IsConstantArrayLengthAtMostOne(IOperation dimension)
    {
        if (!dimension.ConstantValue.HasValue)
            return false;

        return dimension.ConstantValue.Value switch
        {
            int value => value is 0 or 1,
            uint value => value <= 1,
            long value => value is 0 or 1,
            ulong value => value <= 1,
            _ => false
        };
    }

    private static bool IsSymbolDeclaredInside(ISymbol symbol, SyntaxNode container)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource && container.Span.Contains(location.SourceSpan))
                return true;
        }

        return false;
    }

    private static bool IsOriginDeclaredInside(ContextOrigin origin, SyntaxNode container)
    {
        return IsSymbolDeclaredInside(origin.Symbol, container) ||
               origin.ReceiverSymbol != null &&
               IsSymbolDeclaredInside(origin.ReceiverSymbol, container);
    }
}
