using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A <c>while</c>, <c>do</c> or <c>for</c> loop that runs until the database says it is done issues one query per
/// batch or attempt, not one per item: keyset and paged batches, batched deletes, drain loops, polling loops and
/// catch-guarded retries. Those loops are exempt when their condition does not walk an item source of its own
/// (a queue, a reader, an indexed collection). <c>foreach</c> loops walk items and are exempt only over
/// <c>Chunk(...)</c> batches that the query reads as a whole.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private const int MaxDerivedLocalPasses = 8;

    private static bool IsBatchPollingOrRetryLoop(
        IInvocationOperation invocation,
        ILoopOperation loop,
        CancellationToken cancellationToken)
    {
        IOperation? condition;
        switch (loop)
        {
            case IForEachLoopOperation forEach:
                return IsChunkedBatch(invocation, forEach);
            case IWhileLoopOperation whileLoop:
                condition = whileLoop.Condition;
                break;
            case IForLoopOperation forLoop:
                condition = forLoop.Condition;
                break;
            default:
                return false;
        }

        if (IsCatchGuardedRetryAttempt(invocation, loop))
            return true;

        if (loop is IWhileLoopOperation && IsTakeBoundedDrainLoop(loop, condition, cancellationToken))
            return true;

        // A drain loop (`while (await q.AnyAsync()) { ... }`) asks the database whether to go on, so the work
        // inside it runs once per batch the database reports, not once per item of some other source.
        var conditionQueriesDatabase = condition != null && ConditionExecutesDatabaseWork(condition, cancellationToken);

        var resultLocals = GetResultDerivedLocals(invocation, loop);
        AddLevelFrontiers(invocation, loop, resultLocals);
        var invocationInCondition = condition != null && condition.Syntax.Span.Contains(invocation.Syntax.Span);

        if (!IsConditionFreeOfItemSources(condition, resultLocals, cancellationToken))
            return false;

        return invocationInCondition ||
               conditionQueriesDatabase ||
               ResultControlsLoopExit(loop, condition, resultLocals) ||
               IsPagedByLoopCounter(invocation, loop) ||
               IsPollingLoop(loop, condition);
    }

    /// <summary>
    /// The loop works through the data a batch at a time: a <c>foreach</c> over <c>Chunk(...)</c>, or a loop with
    /// a query of its own that LC007 exempts as a batch, drain or polling query. LC010 uses this to accept one
    /// <c>SaveChanges</c> per batch, which keeps the change tracker small on large jobs.
    /// </summary>
    internal static bool IsBatchLoop(ILoopOperation loop, CancellationToken cancellationToken)
    {
        if (loop is IForEachLoopOperation forEach)
            return IsChunkLoop(forEach);

        foreach (var operation in DescendantsInSameBody(loop))
        {
            if (operation is IInvocationOperation invocation &&
                invocation.TargetMethod.Name is not ("SaveChanges" or "SaveChangesAsync") &&
                invocation.FindEnclosingLoop() == loop &&
                TryMatchDatabaseExecution(invocation, cancellationToken, out _) &&
                !IsCatchGuardedRetryAttempt(invocation, loop) &&
                IsBatchPollingOrRetryLoop(invocation, loop, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsChunkLoop(IForEachLoopOperation forEach)
    {
        var collection = forEach.Collection;
        while (collection is IConversionOperation conversion)
            collection = conversion.Operand;

        return collection is IInvocationOperation chunk &&
               IsMethod(chunk.TargetMethod.ReducedFrom ?? chunk.TargetMethod, "System.Linq", "Enumerable", "Chunk") &&
               forEach.Locals.Length == 1;
    }

    /// <summary>
    /// <c>foreach (var batch in ids.Chunk(500))</c> whose query reads the whole batch (<c>batch.Contains(x.Id)</c>,
    /// the batch passed as an argument, or a loop local built from it such as <c>batch.ToHashSet()</c>) runs one
    /// query per chunk, the usual way to keep an <c>IN</c> list under the provider's parameter limit.
    /// </summary>
    private static bool IsChunkedBatch(IInvocationOperation invocation, IForEachLoopOperation forEach)
    {
        return IsChunkLoop(forEach) &&
               QueryReadsWholeFrontier(invocation, forEach.Locals[0], CollectLocalWrites(forEach.Body));
    }

    /// <summary>
    /// A page per iteration: the query's <c>Skip</c> count reads a local the loop advances
    /// (<c>Skip(batch * size).Take(size)</c>), and <c>Take</c> reads more than one row.
    /// </summary>
    private static bool IsPagedByLoopCounter(IInvocationOperation invocation, ILoopOperation loop)
    {
        var counters = new HashSet<ILocalSymbol>(loop.Locals, SymbolEqualityComparer.Default);
        var advanced = loop is IForLoopOperation { AtLoopBottom: var atLoopBottom }
            ? DescendantsInSameBody(loop.Body).Concat(atLoopBottom.SelectMany(DescendantsInSameBody))
            : DescendantsInSameBody(loop.Body);
        foreach (var operation in advanced)
        {
            switch (operation)
            {
                case IAssignmentOperation { Target: ILocalReferenceOperation target }:
                    counters.Add(target.Local);
                    break;
                case IIncrementOrDecrementOperation { Target: ILocalReferenceOperation target }:
                    counters.Add(target.Local);
                    break;
            }
        }

        var skipsByCounter = false;
        var takesPage = false;
        for (var current = invocation.GetInvocationReceiver()?.UnwrapConversions();
             current is IInvocationOperation call;
             current = call.GetInvocationReceiver()?.UnwrapConversions())
        {
            if (call.Arguments.Length == 0)
                continue;

            var count = call.Arguments[call.Arguments.Length - 1].Value;
            switch (call.TargetMethod.Name)
            {
                case "Skip":
                    skipsByCounter |= ReferencesAnyLocal(count, counters);
                    break;
                case "Take":
                    takesPage |= count.ConstantValue is not { HasValue: true, Value: int rows } || rows > 1;
                    break;
            }
        }

        return skipsByCounter && takesPage;
    }

    /// <summary>
    /// The locals inside the loop that hold the execution's result, or a value computed from it
    /// (<c>hasMore = batch.Count == size</c>, <c>last = batch[^1].Id</c>).
    /// </summary>
    private static HashSet<ILocalSymbol> GetResultDerivedLocals(IInvocationOperation invocation, ILoopOperation loop)
    {
        var locals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        var value = (IOperation)invocation;
        while (value.Parent is IAwaitOperation or IConversionOperation ||
               value.Parent is IInvocationOperation { TargetMethod.Name: "ConfigureAwait" } configureAwait &&
               configureAwait.Instance == value)
        {
            value = value.Parent;
        }

        switch (value.Parent)
        {
            case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }:
                locals.Add(declarator.Symbol);
                break;
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                when assignment.Value == value:
                locals.Add(target.Local);
                break;
        }

        if (locals.Count == 0)
            return locals;

        var writes = CollectLocalWrites(loop.Body);
        for (var pass = 0; pass < MaxDerivedLocalPasses; pass++)
        {
            var added = false;
            foreach (var (local, written) in writes)
            {
                if (!locals.Contains(local) && ReferencesAnyLocal(written, locals))
                {
                    locals.Add(local);
                    added = true;
                }
            }

            if (!added)
                break;
        }

        return locals;
    }

    /// <summary>
    /// A hierarchy walked one level per query: <c>while (frontier.Count &gt; 0)</c>, where the query reads the whole
    /// frontier (<c>frontier.Contains(x.ParentId)</c>, or the frontier passed as an argument) and the frontier is
    /// refilled from the result (<c>foreach (var id in next) frontier.Add(id);</c>). Such a frontier counts as a
    /// result-derived local. A worklist the query takes one item from (<c>Dequeue()</c>, <c>Pop()</c>,
    /// <c>frontier[0]</c>) is still one query per item and does not.
    /// </summary>
    private static void AddLevelFrontiers(IInvocationOperation invocation, ILoopOperation loop, HashSet<ILocalSymbol> resultLocals)
    {
        if (resultLocals.Count == 0)
            return;

        // Elements of the result: foreach variables over it (or over LINQ on it).
        var resultValues = new HashSet<ILocalSymbol>(resultLocals, SymbolEqualityComparer.Default);
        foreach (var operation in DescendantsInSameBody(loop.Body))
        {
            if (operation is IForEachLoopOperation forEach && ReferencesAnyLocal(forEach.Collection, resultValues))
            {
                foreach (var local in forEach.Locals)
                    resultValues.Add(local);
            }
        }

        var writes = CollectLocalWrites(loop.Body);
        foreach (var operation in DescendantsInSameBody(loop.Body))
        {
            if (operation is IInvocationOperation
                {
                    TargetMethod.Name: "Add" or "AddRange" or "Enqueue" or "Push" or "UnionWith",
                    Instance: ILocalReferenceOperation { Local: var frontier }
                } refill &&
                !resultLocals.Contains(frontier) &&
                refill.Arguments.Any(argument => ReferencesAnyLocal(argument.Value, resultValues)) &&
                QueryReadsWholeFrontier(invocation, frontier, writes))
            {
                resultLocals.Add(frontier);
            }
        }
    }

    /// <summary>
    /// The query depends on the frontier only as a whole set: every reference to it, in the query or in the loop
    /// locals the query is built from, is an argument or the receiver of <c>Contains</c>.
    /// </summary>
    private static bool QueryReadsWholeFrontier(
        IInvocationOperation invocation,
        ILocalSymbol frontier,
        List<(ILocalSymbol Local, IOperation Value)> loopWrites)
    {
        var readsWhole = false;
        var visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IOperation>();
        pending.Push(invocation);

        while (pending.Count > 0)
        {
            foreach (var operation in pending.Pop().DescendantsAndSelf())
            {
                if (operation is not ILocalReferenceOperation localReference)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(localReference.Local, frontier))
                {
                    if (!IsWholeSetUse(localReference))
                        return false;

                    readsWhole = true;
                }
                else if (visited.Add(localReference.Local))
                {
                    foreach (var (local, value) in loopWrites)
                    {
                        if (SymbolEqualityComparer.Default.Equals(local, localReference.Local))
                            pending.Push(value);
                    }
                }
            }
        }

        return readsWhole;
    }

    private static bool IsWholeSetUse(ILocalReferenceOperation reference)
    {
        var parent = reference.Parent;
        while (parent is IConversionOperation)
            parent = parent.Parent;

        return parent is IArgumentOperation { Parent: not IInvocationOperation { TargetMethod.Name: "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Last" or "LastOrDefault" or "ElementAt" or "ElementAtOrDefault" } } ||
               parent is IInvocationOperation { TargetMethod.Name: "Contains" } contains && contains.Instance?.Syntax == reference.Syntax;
    }

    private static List<(ILocalSymbol Local, IOperation Value)> CollectLocalWrites(IOperation body)
    {
        var writes = new List<(ILocalSymbol, IOperation)>();
        foreach (var operation in DescendantsInSameBody(body))
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation { Initializer.Value: { } initializerValue } declarator:
                    writes.Add((declarator.Symbol, initializerValue));
                    break;
                case IAssignmentOperation { Target: ILocalReferenceOperation target } assignment:
                    writes.Add((target.Local, assignment.Value));
                    break;
            }
        }

        return writes;
    }

    /// <summary>
    /// The result stops the loop: it is read by the loop condition, or by an <c>if</c> whose then-branch
    /// breaks out of this loop or returns.
    /// </summary>
    private static bool ResultControlsLoopExit(ILoopOperation loop, IOperation? condition, HashSet<ILocalSymbol> resultLocals)
    {
        if (resultLocals.Count == 0)
            return false;

        if (condition != null && ReferencesAnyLocal(condition, resultLocals))
            return true;

        foreach (var operation in DescendantsInSameBody(loop.Body))
        {
            if (operation is IConditionalOperation { WhenTrue: { } whenTrue } conditional &&
                ReferencesAnyLocal(conditional.Condition, resultLocals) &&
                ExitsLoop(whenTrue, loop))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExitsLoop(IOperation branch, ILoopOperation loop)
    {
        return DescendantsInSameBody(branch).Any(operation => operation switch
        {
            IBranchOperation { BranchKind: BranchKind.Break } breakOperation =>
                SymbolEqualityComparer.Default.Equals(breakOperation.Target, loop.ExitLabel),
            IReturnOperation => true,
            _ => false
        });
    }

    private static bool IsPollingLoop(ILoopOperation loop, IOperation? condition)
    {
        if (condition != null &&
            DescendantsInSameBody(condition).Any(operation =>
                operation is IInvocationOperation invocation &&
                IsMethod(invocation.TargetMethod, "System.Threading", "PeriodicTimer", "WaitForNextTickAsync")))
        {
            return true;
        }

        return DescendantsInSameBody(loop.Body).Any(operation =>
            operation is IInvocationOperation invocation &&
            (IsMethod(invocation.TargetMethod, "System.Threading.Tasks", "Task", "Delay") ||
             IsMethod(invocation.TargetMethod, "System.Threading", "Thread", "Sleep")));
    }

    /// <summary>
    /// The loop condition only reads constants, counters, flags, cancellation, the execution itself and values
    /// derived from its result. Any other call, indexer or collection member means the loop walks items of its own.
    /// </summary>
    private static bool IsConditionFreeOfItemSources(
        IOperation? condition,
        HashSet<ILocalSymbol> resultLocals,
        CancellationToken cancellationToken)
    {
        if (condition == null)
            return true;

        var pending = new Stack<IOperation>();
        pending.Push(condition);
        while (pending.Count > 0)
        {
            var operation = pending.Pop();
            if (operation.ConstantValue.HasValue ||
                operation is IInvocationOperation execution && TryMatchDatabaseExecution(execution, cancellationToken, out _))
                continue;

            switch (operation)
            {
                case IBinaryOperation:
                case IUnaryOperation:
                case IConversionOperation:
                case IAwaitOperation:
                case IArgumentOperation:
                case IIsPatternOperation:
                case IPatternOperation:
                    break;
                case ILocalReferenceOperation localReference:
                    if (!resultLocals.Contains(localReference.Local) && !IsCounterFlagOrCancellation(localReference.Type))
                        return false;
                    break;
                case IParameterReferenceOperation parameterReference:
                    if (!IsCounterFlagOrCancellation(parameterReference.Type))
                        return false;
                    break;
                case IFieldReferenceOperation fieldReference:
                    if (!IsResultMemberAccess(fieldReference.Instance, resultLocals) &&
                        !IsCounterFlagOrCancellation(fieldReference.Type) &&
                        !IsConfiguredCount(fieldReference))
                        return false;
                    continue;
                case IPropertyReferenceOperation propertyReference:
                    if (!IsResultMemberAccess(propertyReference.Instance, resultLocals) &&
                        !IsCancellationMember(propertyReference) &&
                        !(propertyReference.Arguments.Length == 0 && IsConfiguredCount(propertyReference)))
                        return false;
                    continue;
                case IInvocationOperation invocation:
                    if (!IsMethod(invocation.TargetMethod, "System.Threading", "PeriodicTimer", "WaitForNextTickAsync") &&
                        !IsLinqToObjectsOverResult(invocation, resultLocals))
                        return false;

                    // The timer or the result list is the receiver; only the arguments still need checking.
                    foreach (var argument in invocation.Arguments.Skip(invocation.Instance == null ? 1 : 0))
                        pending.Push(argument);
                    continue;
                default:
                    return false;
            }

            foreach (var child in operation.ChildOperations)
                pending.Push(child);
        }

        return true;
    }

    private static bool ConditionExecutesDatabaseWork(IOperation condition, CancellationToken cancellationToken)
    {
        return DescendantsInSameBody(condition).Any(operation =>
            operation is IInvocationOperation execution &&
            TryMatchDatabaseExecution(execution, cancellationToken, out _));
    }

    private static bool IsResultMemberAccess(IOperation? instance, HashSet<ILocalSymbol> resultLocals)
    {
        return instance is ILocalReferenceOperation localReference && resultLocals.Contains(localReference.Local);
    }

    private static bool IsCancellationMember(IPropertyReferenceOperation propertyReference)
    {
        return IsCancellationType(propertyReference.Property.ContainingType) ||
               IsCancellationType(propertyReference.Type);
    }

    private static bool IsLinqToObjectsOverResult(IInvocationOperation invocation, HashSet<ILocalSymbol> resultLocals)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (!IsMethod(method, "System.Linq", "Enumerable", method.Name) || invocation.Arguments.Length == 0)
            return false;

        var source = invocation.Arguments[0].Value;
        while (source is IConversionOperation conversion)
            source = conversion.Operand;

        return source is ILocalReferenceOperation localReference && resultLocals.Contains(localReference.Local);
    }

    private static bool IsCounterFlagOrCancellation(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return true;
        }

        return IsCancellationType(type);
    }

    private static bool IsCancellationType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "CancellationToken" or "CancellationTokenSource" } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    /// <summary>
    /// The execution sits in a <c>try</c> with a <c>catch</c>, and the <c>try</c> leaves the loop after it succeeds.
    /// </summary>
    private static bool IsCatchGuardedRetryAttempt(IInvocationOperation invocation, ILoopOperation loop)
    {
        IOperation child = invocation;
        for (var current = invocation.Parent; current != null && current != loop; current = current.Parent)
        {
            if (current is ITryOperation { Catches.Length: > 0 } tryOperation && child == tryOperation.Body)
            {
                var statement = (IOperation)invocation;
                while (statement.Parent != null && statement.Parent != tryOperation.Body)
                    statement = statement.Parent;

                var statements = tryOperation.Body.Operations;
                var index = statements.IndexOf(statement);
                return index >= 0 && statements.Skip(index + 1).Any(next => next switch
                {
                    IBranchOperation { BranchKind: BranchKind.Break } breakOperation =>
                        SymbolEqualityComparer.Default.Equals(breakOperation.Target, loop.ExitLabel),
                    IReturnOperation => true,
                    _ => false
                });
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;

            child = current;
        }

        return false;
    }

    private static bool ReferencesAnyLocal(IOperation operation, HashSet<ILocalSymbol> locals)
    {
        return DescendantsInSameBody(operation).Any(descendant =>
            descendant is ILocalReferenceOperation localReference && locals.Contains(localReference.Local));
    }

    private static bool IsMethod(IMethodSymbol method, string containingNamespace, string containingType, string name)
    {
        return method.Name == name &&
               method.ContainingType?.Name == containingType &&
               method.ContainingType.ContainingNamespace?.ToDisplayString() == containingNamespace;
    }

    /// <summary>The operation and its descendants, without entering lambdas or local functions.</summary>
    private static IEnumerable<IOperation> DescendantsInSameBody(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var operation = pending.Pop();
            yield return operation;

            foreach (var child in operation.ChildOperations)
            {
                if (child is not IAnonymousFunctionOperation and not ILocalFunctionOperation)
                    pending.Push(child);
            }
        }
    }
}
