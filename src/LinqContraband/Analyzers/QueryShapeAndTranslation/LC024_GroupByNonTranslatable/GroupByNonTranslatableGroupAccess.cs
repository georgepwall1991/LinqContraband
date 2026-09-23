using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC024_GroupByNonTranslatable;

public sealed partial class GroupByNonTranslatableAnalyzer
{
    // Group operators that EF Core can translate as part of a server-side aggregate chain
    // (e.g. g.Where(p).Count(), g.Select(s).Sum(), g.Distinct().Count()).
    private static readonly ImmutableHashSet<string> TranslatableGroupOperators = ImmutableHashSet.Create(
        "Where", "Select", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Distinct");

    private static bool IsAllowedAggregateMethod(string methodName)
    {
        return methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max" or "Any" or "All"
            or "CountAsync" or "LongCountAsync" or "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync"
            or "AnyAsync" or "AllAsync";
    }

    // A group-access invocation is translatable when its receiver chain roots at the grouping
    // parameter through translatable operators AND the OUTERMOST invocation of that chain is an
    // allowed aggregate. EF Core 9 translates filtered/projected group aggregates such as
    // g.Where(p).Count() and g.Select(s).Sum(), but a chain that terminates in a non-aggregate
    // (a bare g.Where(p), a materializer g.Select(s).ToList(), or an element accessor
    // g.OrderBy(s).First()) still returns a sub-sequence or materializes and must be reported.
    private static bool IsTranslatableGroupAccess(IInvocationOperation invocation, IParameterSymbol groupParam)
    {
        if (!RootsAtGroupParam(invocation.GetInvocationReceiver(), groupParam))
            return false;

        var terminal = FindOutermostGroupChainInvocation(invocation);
        if (!IsAllowedAggregateMethod(terminal.TargetMethod.Name) ||
            !IsKnownAggregateContainingType(terminal.TargetMethod.ContainingType))
        {
            return false;
        }

        // The chain operators are translatable, but their predicate/selector lambda bodies must be
        // too. Rather than guess which BCL/user method calls EF can translate, this stays
        // deliberately conservative: the chain is exempt only when its lambda bodies are
        // invocation-free (member access, comparisons, arithmetic). ANY method call inside a
        // predicate/selector keeps the chain reported. The terminal subtree contains every lambda in the chain.
        return !ChainHasLambdaInvocation(terminal, groupParam);
    }

    // True when the group-chain subtree contains an invocation that is NOT one of the chain's own
    // translatable group operators rooted at the grouping parameter.
    private static bool ChainHasLambdaInvocation(IInvocationOperation terminal, IParameterSymbol groupParam)
    {
        foreach (var descendant in GetAllOperations(terminal).OfType<IInvocationOperation>())
        {
            if (IsTranslatableScalarConversion(descendant.TargetMethod))
                continue;

            if (!IsGroupChainMethod(descendant.TargetMethod) ||
                !RootsAtGroupParam(descendant.GetInvocationReceiver(), groupParam))
            {
                return true;
            }
        }

        return false;
    }

    // `Convert.ToInt32(c.ReadOnly)` inside an aggregate selector, as in Bitwarden's
    // `Convert.ToBoolean(g.Min(c => Convert.ToInt32(c.ReadOnly)))`. EF Core's relational providers
    // translate the single-argument System.Convert conversions to SQL casts.
    private static bool IsTranslatableScalarConversion(IMethodSymbol method)
    {
        return method.ContainingType?.ToDisplayString() == "System.Convert" &&
               method.Parameters.Length == 1 &&
               method.Name is "ToBoolean" or "ToByte" or "ToDecimal" or "ToDouble" or "ToInt16" or "ToInt32" or "ToInt64" or "ToString";
    }

    // An invocation that only sees the group through translatable aggregates, such as
    // `Convert.ToBoolean(g.Min(...))` or `Math.Round(g.Average(...))`, works on the aggregate's
    // scalar result, not on the group's elements.
    private static bool ReferencesGroupOnlyThroughAggregates(IInvocationOperation invocation, IParameterSymbol groupParam)
    {
        var aggregates = GetAllOperations(invocation)
            .OfType<IInvocationOperation>()
            .Where(candidate => !ReferenceEquals(candidate, invocation) && IsTranslatableGroupAccess(candidate, groupParam))
            .ToList();

        foreach (var reference in GetAllOperations(invocation).OfType<IParameterReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Parameter, groupParam))
                continue;

            if (!aggregates.Any(aggregate => aggregate.Syntax.Span.Contains(reference.Syntax.Span)))
                return false;
        }

        return true;
    }

    private static bool RootsAtGroupParam(IOperation? receiver, IParameterSymbol groupParam)
    {
        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is IParameterReferenceOperation parameterReference)
                return SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, groupParam);

            if (current is IInvocationOperation chained && IsGroupChainMethod(chained.TargetMethod))
            {
                current = chained.GetInvocationReceiver();
                continue;
            }

            return false;
        }

        return false;
    }

    private static IInvocationOperation FindOutermostGroupChainInvocation(IInvocationOperation invocation)
    {
        var outermost = invocation;
        while (true)
        {
            IOperation? parent = outermost.Parent;
            while (parent is IConversionOperation or IArgumentOperation)
                parent = parent.Parent;

            if (parent is IInvocationOperation parentInvocation &&
                IsGroupChainMethod(parentInvocation.TargetMethod) &&
                ReferenceEquals(parentInvocation.GetInvocationReceiver()?.UnwrapConversions(), outermost))
            {
                outermost = parentInvocation;
                continue;
            }

            return outermost;
        }
    }

    private static bool IsGroupChainMethod(IMethodSymbol method)
    {
        return (IsAllowedAggregateMethod(method.Name) || TranslatableGroupOperators.Contains(method.Name)) &&
               IsKnownAggregateContainingType(method.ContainingType);
    }

    private static bool IsKnownAggregateContainingType(INamedTypeSymbol? containingType)
    {
        var containingNamespace = containingType?.ContainingNamespace?.ToString();
        return containingNamespace == "System.Linq" && containingType is { Name: "Enumerable" or "Queryable" } ||
               containingNamespace == "Microsoft.EntityFrameworkCore" && containingType?.Name == "EntityFrameworkQueryableExtensions";
    }
}
