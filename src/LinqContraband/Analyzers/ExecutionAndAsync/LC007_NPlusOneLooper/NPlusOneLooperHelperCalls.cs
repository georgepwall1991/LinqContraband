using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal sealed class NPlusOneHelperCallMatch
{
    public NPlusOneHelperCallMatch(
        string helperName,
        string queryMethodName,
        string loopKind,
        string? operatorName = null)
    {
        HelperName = helperName;
        QueryMethodName = queryMethodName;
        LoopKind = loopKind;
        OperatorName = operatorName;
    }

    public string HelperName { get; }
    public string QueryMethodName { get; }
    public string LoopKind { get; }

    /// <summary>The per-element operator whose lambda calls the helper, or null when a loop calls it.</summary>
    public string? OperatorName { get; }
}

/// <summary>
/// A loop that calls a helper method which runs the query is the most common hidden N+1. When a loop calls a method
/// whose source is in this compilation, LC007 looks into its body, up to <see cref="MaxHelperDepth"/> calls deep,
/// for a database execution it would report if it were written in the loop, and reports the call. An interface,
/// abstract or virtual call is followed into every implementation declared here (see <c>GetHelperTargets</c>) and
/// reported only when all of them run a query.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    internal const int MaxHelperDepth = 3;

    public static NPlusOneHelperCallMatch? AnalyzeHelperCall(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        var inLoop = invocation.FindEnclosingLoop() != null;
        if (!inLoop && FindPerElementOperator(invocation, out _) == null)
            return null;

        var queryMethodName = GetCallExecution(invocation, MaxHelperDepth, caller: null, cache, cancellationToken);
        if (queryMethodName.Length == 0)
            return null;

        var helperName = invocation.TargetMethod.Name;
        var loop = inLoop ? FindPerIterationLoopForHelperCall(invocation, cache, cancellationToken) : null;
        if (loop != null)
            return new NPlusOneHelperCallMatch(helperName, queryMethodName, loop.GetLoopKind());

        // A helper called from a lambda that an in-memory operator runs once per element.
        var perElementOperator = FindPerElementOperator(invocation, out _);
        return perElementOperator != null
            ? new NPlusOneHelperCallMatch(
                helperName,
                queryMethodName,
                NPlusOneLooperDiagnosticProperties.LambdaLoopKind,
                perElementOperator.TargetMethod.Name)
            : null;
    }

    /// <summary>
    /// A helper passed as a method group to a per-element operator (<c>ids.Select(repository.Load)</c>) runs once
    /// per element, exactly like <c>ids.Select(id => repository.Load(id))</c>.
    /// </summary>
    public static NPlusOneHelperCallMatch? AnalyzeHelperMethodGroup(
        IMethodReferenceOperation methodReference,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        var perElementOperator = FindPerElementOperatorTakingDelegate(methodReference);
        if (perElementOperator == null)
            return null;

        var targets = GetHelperTargets(
            methodReference.Method,
            methodReference.IsVirtual,
            methodReference.Instance?.Type,
            cache,
            cancellationToken);
        var queryMethodName = GetExecution(targets, MaxHelperDepth, caller: null, cache, cancellationToken);
        return queryMethodName.Length == 0
            ? null
            : new NPlusOneHelperCallMatch(
                methodReference.Method.Name,
                queryMethodName,
                NPlusOneLooperDiagnosticProperties.LambdaLoopKind,
                perElementOperator.TargetMethod.Name);
    }

    /// <summary>
    /// The same loop walk as a direct execution, with one more exemption: a <c>while</c>, <c>do</c> or <c>for</c>
    /// whose condition runs a query, directly or through a helper, is a drain loop and runs once per batch.
    /// </summary>
    private static ILoopOperation? FindPerIterationLoopForHelperCall(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is ILoopOperation loop &&
                invocation.SharesOwningExecutableRoot(loop) &&
                IsPerIterationInvocation(invocation, loop) &&
                !loop.RunsAtMostOnce() &&
                !loop.IteratesItemsFixedInSource(cancellationToken) &&
                !invocation.LeavesLoopAfter(loop) &&
                !IsBatchPollingOrRetryLoop(invocation, loop, cancellationToken) &&
                !ConditionRunsQueryThroughHelper(loop, cache, cancellationToken) &&
                !IsLoadOnceGuardedInLoop(invocation, loop))
            {
                return loop;
            }
        }

        return null;
    }

    private static bool ConditionRunsQueryThroughHelper(
        ILoopOperation loop,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        var condition = loop switch
        {
            IWhileLoopOperation whileLoop => whileLoop.Condition,
            IForLoopOperation forLoop => forLoop.Condition,
            _ => null
        };
        if (condition == null)
            return false;

        foreach (var operation in DescendantsInSameBody(condition))
        {
            if (operation is not IInvocationOperation call)
                continue;

            if (TryMatchDatabaseExecution(call, cancellationToken, out _))
                return true;

            if (GetCallExecution(call, MaxHelperDepth, caller: null, cache, cancellationToken).Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The query method the helper runs once per call, or <see cref="NPlusOneLooperHelperCache.NoExecution"/>.
    /// Depth bounds the walk (a helper at depth 1 is read only for its own executions), which also ends recursion
    /// cycles; each (helper, depth) pair is summarized once per compilation.
    /// </summary>
    private static string GetHelperExecution(
        IMethodSymbol helper,
        int depth,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetSummary(helper, depth, out var cached))
            return cached;

        cancellationToken.ThrowIfCancellationRequested();
        return cache.StoreSummary(helper, depth, SummarizeHelper(helper, depth, cache, cancellationToken));
    }

    private static string SummarizeHelper(
        IMethodSymbol helper,
        int depth,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        if (!TryGetHelperBody(helper, cache, cancellationToken, out var root))
            return NPlusOneLooperHelperCache.NoExecution;

        string? queryMethodName = null;
        var firstExecutionStart = int.MaxValue;

        // Lambdas and local functions the helper only declares (cache factories, callbacks) are not walked.
        foreach (var operation in DescendantsInSameBody(root))
        {
            if (operation is not IInvocationOperation call || call.Syntax.SpanStart >= firstExecutionStart)
                continue;

            // An execution in a loop of the helper is already reported there, or is exempt as a batch loop.
            if (IsInsidePerIterationLoopOf(call, root))
                continue;

            string? found = null;
            if (cache.MatchExecution != null)
                found = cache.MatchExecution(call, cancellationToken);
            else if (TryMatchDatabaseExecution(call, cancellationToken, out var match))
                found = match.MethodName;

            if (found == null && depth > 1)
            {
                var nested = GetCallExecution(call, depth - 1, helper, cache, cancellationToken);
                if (nested.Length > 0)
                    found = nested;
            }

            if (found != null)
            {
                queryMethodName = found;
                firstExecutionStart = call.Syntax.SpanStart;
            }
        }

        if (queryMethodName == null || (cache.SkipMemoizedHelpers && IsMemoized(root, firstExecutionStart)))
            return NPlusOneLooperHelperCache.NoExecution;

        return queryMethodName;
    }

    private static bool TryGetHelperBody(
        IMethodSymbol helper,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        out IOperation root)
    {
        root = null!;
        foreach (var reference in helper.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            var hasBody = syntax switch
            {
                BaseMethodDeclarationSyntax method => method.Body != null || method.ExpressionBody != null,
                LocalFunctionStatementSyntax localFunction => localFunction.Body != null || localFunction.ExpressionBody != null,
                _ => false
            };
            if (!hasBody)
                continue;

            if (!cache.TryGetSemanticModel(syntax.SyntaxTree, out var semanticModel))
                return false;

            var operation = semanticModel.GetOperation(syntax, cancellationToken);
            if (operation is IMethodBodyBaseOperation or ILocalFunctionOperation)
            {
                root = operation;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsInsidePerIterationLoopOf(IInvocationOperation call, IOperation root)
    {
        for (var current = call.Parent; current != null && current != root; current = current.Parent)
        {
            if (current is ILoopOperation loop && IsPerIterationInvocation(call, loop))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The helper consults a cache or remembered state before querying, so repeated calls usually do not reach the
    /// database: any cache API call (<c>TryGetValue</c>, <c>ContainsKey</c>, <c>GetOrAdd</c>, a <c>*Cache*</c> type),
    /// a <c>??=</c> or <c>??</c> over a field or property, or an <c>if</c> before the query that reads a field or a
    /// property of the helper's type and returns.
    /// </summary>
    private static bool IsMemoized(IOperation root, int firstExecutionStart)
    {
        foreach (var operation in DescendantsInSameBody(root))
        {
            switch (operation)
            {
                case IInvocationOperation invocation when IsCacheLookup(invocation):
                    return true;
                case ICoalesceAssignmentOperation { Target: var target } when IsMemberState(target):
                    return true;
                case ICoalesceOperation { Value: var value } when IsMemberState(value.UnwrapConversions()):
                    return true;
                case IConditionalOperation { WhenTrue: var whenTrue } conditional
                    when conditional.Syntax.SpanStart < firstExecutionStart &&
                         ReadsMemberState(conditional.Condition) &&
                         Returns(whenTrue):
                    return true;
            }
        }

        return false;
    }

    private static bool IsCacheLookup(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.Name is "TryGetValue" or "ContainsKey" or "GetOrAdd" or "GetOrCreate" or "GetOrCreateAsync" or "TryGet")
            return true;

        return method.ContainingType?.Name.Contains("Cache") == true ||
               invocation.Instance?.Type?.Name.Contains("Cache") == true;
    }

    /// <summary>
    /// A field, or a property of the helper's own type or a static one, that is not the context or a query:
    /// <c>_db.Users</c> in a condition is a query, not remembered state.
    /// </summary>
    private static bool IsMemberState(IOperation? operation)
    {
        var isMember = operation switch
        {
            IFieldReferenceOperation field => !field.Field.IsConst,
            IPropertyReferenceOperation property => property.Instance is null or IInstanceReferenceOperation,
            _ => false
        };

        return isMember &&
               operation!.Type is { } type &&
               !type.IsDbContext() &&
               !type.IsDbSet() &&
               !type.IsIQueryable();
    }

    private static bool ReadsMemberState(IOperation condition)
    {
        foreach (var operation in DescendantsInSameBody(condition))
        {
            if (IsMemberState(operation))
                return true;
        }

        return false;
    }

    private static bool Returns(IOperation branch)
    {
        foreach (var operation in DescendantsInSameBody(branch))
        {
            if (operation is IReturnOperation)
                return true;
        }

        return false;
    }
}
