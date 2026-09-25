using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal sealed class NPlusOneHelperCallMatch
{
    public NPlusOneHelperCallMatch(string helperName, string queryMethodName, string loopKind)
    {
        HelperName = helperName;
        QueryMethodName = queryMethodName;
        LoopKind = loopKind;
    }

    public string HelperName { get; }
    public string QueryMethodName { get; }
    public string LoopKind { get; }
}

/// <summary>
/// A loop that calls a helper method which runs the query is the most common hidden N+1. When a loop calls a method
/// whose source is in this compilation and whose target is fixed at compile time (not an override that might not
/// query), LC007 looks into its body, up to <see cref="MaxHelperDepth"/> calls deep, for a database execution it
/// would report if it were written in the loop, and reports the call.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    internal const int MaxHelperDepth = 3;

    public static NPlusOneHelperCallMatch? AnalyzeHelperCall(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        var helper = GetAnalyzableHelper(invocation);
        if (helper == null || invocation.FindEnclosingLoop() == null)
            return null;

        var queryMethodName = GetHelperExecution(helper, MaxHelperDepth, cache, cancellationToken);
        if (queryMethodName.Length == 0)
            return null;

        var loop = FindPerIterationLoopForHelperCall(invocation, cache, cancellationToken);
        if (loop == null)
            return null;

        return new NPlusOneHelperCallMatch(helper.Name, queryMethodName, loop.GetLoopKind());
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
                !IsBatchPollingOrRetryLoop(invocation, loop, cancellationToken) &&
                !ConditionRunsQueryThroughHelper(loop, cache, cancellationToken))
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

            var helper = GetAnalyzableHelper(call);
            if (helper != null && GetHelperExecution(helper, MaxHelperDepth, cache, cancellationToken).Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The method a call statically binds to, when LC007 can read its body: an ordinary method, extension method or
    /// local function declared in source. Abstract, interface and overridable methods are skipped, since an override
    /// might not query, unless the method or its type is sealed or static.
    /// </summary>
    private static IMethodSymbol? GetAnalyzableHelper(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        method = method.OriginalDefinition;
        method = method.PartialImplementationPart ?? method;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) ||
            method.IsAbstract ||
            method.IsExtern ||
            method.DeclaringSyntaxReferences.Length == 0)
        {
            return null;
        }

        if (method.MethodKind == MethodKind.Ordinary)
        {
            var containingType = method.ContainingType;
            if (containingType == null || containingType.TypeKind == TypeKind.Interface)
                return null;

            if ((method.IsVirtual || method.IsOverride) &&
                !method.IsSealed &&
                !containingType.IsSealed &&
                !containingType.IsStatic)
            {
                return null;
            }
        }

        return method;
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
            if (TryMatchDatabaseExecution(call, cancellationToken, out var match))
            {
                found = match.MethodName;
            }
            else if (depth > 1)
            {
                var callee = GetAnalyzableHelper(call);
                if (callee != null && !SymbolEqualityComparer.Default.Equals(callee, helper))
                {
                    var nested = GetHelperExecution(callee, depth - 1, cache, cancellationToken);
                    if (nested.Length > 0)
                        found = nested;
                }
            }

            if (found != null)
            {
                queryMethodName = found;
                firstExecutionStart = call.Syntax.SpanStart;
            }
        }

        if (queryMethodName == null || IsMemoized(root, firstExecutionStart))
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
