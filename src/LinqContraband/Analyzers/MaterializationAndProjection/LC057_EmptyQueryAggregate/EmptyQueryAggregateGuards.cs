using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC057_EmptyQueryAggregate;

public sealed partial class EmptyQueryAggregateAnalyzer
{
    /// <summary>
    /// True when the same member checks the query for rows before the aggregate runs: an <c>Any</c>,
    /// <c>AnyAsync</c>, <c>Count</c>, <c>CountAsync</c>, <c>LongCount</c> or <c>LongCountAsync</c> call earlier in
    /// the code whose query starts from the same local, or from the same expression, as the aggregate's source.
    /// That covers <c>if (q.Any())</c>, <c>if (!q.Any()) return;</c>, <c>q.Any() ? q.Max(...) : 0</c> and
    /// <c>var count = q.Count(); if (count == 0) return;</c>. How the result is used is not checked: the rule
    /// prefers a missed report to a false one.
    /// </summary>
    private static bool HasNonEmptyGuard(IInvocationOperation aggregate, IOperation source)
    {
        var root = (IOperation)aggregate;
        while (root.Parent != null)
            root = root.Parent;

        var aggregateStart = aggregate.Syntax.SpanStart;
        var sourceRoot = GetQueryRoot(source);

        foreach (var descendant in root.Descendants())
        {
            if (descendant is not IInvocationOperation candidate ||
                candidate.Syntax.SpanStart >= aggregateStart ||
                !IsRowCheck(candidate.TargetMethod))
            {
                continue;
            }

            var candidateSource = candidate.GetInvocationReceiver();
            if (candidateSource != null && IsSameQueryRoot(GetQueryRoot(candidateSource), sourceRoot))
                return true;
        }

        return false;
    }

    private static bool IsRowCheck(IMethodSymbol method)
    {
        return method.Name is "Any" or "AnyAsync" or "Count" or "CountAsync" or "LongCount" or "LongCountAsync";
    }

    /// <summary>Walks back through query operators to the local, member or call the query starts from.</summary>
    private static IOperation GetQueryRoot(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        for (var depth = 0; depth < 32; depth++)
        {
            if (current is not IInvocationOperation invocation || !invocation.Type.IsIQueryable())
                break;

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null || !receiver.Type.IsIQueryable() || invocation.TargetMethod.Name == "Set")
                break;

            current = receiver;
        }

        return current;
    }

    private static bool IsSameQueryRoot(IOperation left, IOperation right)
    {
        if (left is ILocalReferenceOperation leftLocal && right is ILocalReferenceOperation rightLocal)
            return SymbolEqualityComparer.Default.Equals(leftLocal.Local, rightLocal.Local);

        return left.Syntax.IsEquivalentTo(right.Syntax, topLevel: false);
    }
}
