using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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
            AnalyzeDirectOverlaps(executableRoot, context);
            AnalyzeSelectorFanOut(executableRoot, context);
        }
    }

    private static void AnalyzeDirectOverlaps(
        IOperation executableRoot,
        OperationBlockAnalysisContext context)
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

            if (!activePrevious.HasValue)
            {
                reportedOrigins.Remove(current.Origin);
                continue;
            }

            if (!reportedOrigins.Add(current.Origin))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    current.Invocation.Syntax.GetLocation(),
                    new[] { activePrevious.Value.Invocation.Syntax.GetLocation() },
                    properties: null,
                    current.Origin.DisplayName));
        }
    }

    private static bool IsDefinitelyActiveBefore(
        IInvocationOperation previous,
        IInvocationOperation current,
        IOperation executableRoot)
    {
        if (previous.Syntax.SpanStart >= current.Syntax.SpanStart ||
            !IsDefinitelyExecutedBefore(previous.Syntax, current.Syntax))
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

        if (EscapesDirectly(previous))
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
            if (IsDefinitelyExecutedBefore(localReference.Syntax, current.Syntax) &&
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
                        !IsDefinitelyExecutedBefore(arrayWhenAllAwait.Syntax, current) ||
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
                out _,
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
            if (operation.Syntax.SpanStart >= beforePosition &&
                !IsInsideNestedExecutable(operation, executableRoot))
            {
                continue;
            }

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
        for (SyntaxNode? current = operation; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, branch) && current is SwitchSectionSyntax)
                return true;

            if (current is IfStatementSyntax or
                ConditionalExpressionSyntax or
                SwitchSectionSyntax or
                SwitchExpressionArmSyntax or
                WhileStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                ForEachVariableStatementSyntax or
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

    private static bool EscapesDirectly(IInvocationOperation invocation)
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

            if (current.Parent is IArgumentOperation argument &&
                argument.Parent is IInvocationOperation consumer)
            {
                return !IsTaskWhenAll(consumer) && !IsTaskWhenAny(consumer);
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
        ILocalReferenceOperation reference,
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
                    !IsDefinitelyExecutedBefore(ifStatement, current))
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
                    !IsDefinitelyExecutedBefore(switchStatement, current))
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
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        default))
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

    private static bool IsDefinitelyExecutedBefore(SyntaxNode previous, SyntaxNode current)
    {
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
                SourceIsAtMostOne(source) ||
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
                    IsOriginDeclaredInside(operation.Origin, selector.Syntax))
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

    private static bool SourceIsAtMostOne(IOperation source)
    {
        source = source.UnwrapConversions();
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
