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

    // EF Core 8+ translates element accessors and group sub-sequences in a GroupBy projection
    // (ROW_NUMBER / a correlated join); checked against EF Core 8 and 9 on SQLite, where
    // g.First(), g.OrderBy(s).FirstOrDefault(), g.Where(p) and g.ToList() all produce SQL.
    // SimpleIdServer FormBuilder's "latest per group" (GroupBy(...).Select(g => g.OrderByDescending(v).First())) relies on it.
    private static readonly ImmutableHashSet<string> GroupProjectionTerminals = ImmutableHashSet.Create(
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault",
        "ElementAt", "ElementAtOrDefault", "ToList", "ToArray");

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
    private static bool IsTranslatableGroupAccess(IInvocationOperation invocation, IParameterSymbol groupParam, bool groupProjections)
    {
        if (!RootsAtGroupParam(invocation.GetInvocationReceiver(), groupParam, groupProjections))
            return false;

        var terminal = FindOutermostGroupChainInvocation(invocation, groupProjections);
        if (!(IsAllowedAggregateMethod(terminal.TargetMethod.Name) ||
              groupProjections && (IsGroupProjectionTerminal(terminal.TargetMethod) ||
                                   IsTranslatableGroupOperator(terminal.TargetMethod))) ||
            !IsKnownAggregateContainingType(terminal.TargetMethod.ContainingType))
        {
            return false;
        }

        // Relational EF Core rejects Last/LastOrDefault without an ordering, so g.Last() stays reported.
        if (groupProjections && UsesLastWithoutOrdering(terminal))
            return false;

        // The chain operators are translatable, but their predicate/selector lambda bodies must be
        // too. Rather than guess which BCL/user method calls EF can translate, this stays
        // deliberately conservative: the chain is exempt only when its lambda bodies are
        // invocation-free (member access, comparisons, arithmetic). ANY method call inside a
        // predicate/selector keeps the chain reported. The terminal subtree contains every lambda in the chain.
        return !ChainHasLambdaInvocation(terminal, groupParam, groupProjections);
    }

    // Only the plain, predicate and int-index overloads were checked; g.FirstOrDefault(defaultValue)
    // and g.ElementAt(^1) are not recognized and stay reported.
    private static bool IsGroupProjectionTerminal(IMethodSymbol method)
    {
        if (!GroupProjectionTerminals.Contains(method.Name))
            return false;

        var parameters = method.IsExtensionMethod && method.ReducedFrom == null
            ? method.Parameters.Skip(1)
            : method.Parameters;
        return parameters.All(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 && method.Name.StartsWith("ElementAt", System.StringComparison.Ordinal) ||
            parameter.Type.TypeKind == TypeKind.Delegate ||
            parameter.Type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expression &&
            expression.TypeArguments[0].TypeKind == TypeKind.Delegate);
    }

    // Last needs an ordering that is still in effect: Distinct drops one made before it.
    private static bool UsesLastWithoutOrdering(IInvocationOperation terminal)
    {
        if (terminal.TargetMethod.Name is not ("Last" or "LastOrDefault"))
            return false;

        var current = terminal.GetInvocationReceiver();
        while (current?.UnwrapConversions() is IInvocationOperation chained)
        {
            switch (chained.TargetMethod.Name)
            {
                case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
                    return false;
                case "Distinct":
                    return true;
            }

            current = chained.GetInvocationReceiver();
        }

        return true;
    }

    // True when the group-chain subtree contains an invocation that is NOT one of the chain's own
    // translatable group operators rooted at the grouping parameter.
    private static bool ChainHasLambdaInvocation(IInvocationOperation terminal, IParameterSymbol groupParam, bool groupProjections)
    {
        foreach (var descendant in GetAllOperations(terminal).OfType<IInvocationOperation>())
        {
            if (IsTranslatableScalarConversion(descendant.TargetMethod))
                continue;

            if (!IsGroupChainMethod(descendant.TargetMethod, groupProjections) ||
                !RootsAtGroupParam(descendant.GetInvocationReceiver(), groupParam, groupProjections) ||
                HasNonLambdaDelegateArgument(descendant))
            {
                return true;
            }
        }

        return false;
    }

    // A predicate or selector held in a variable (g.Where(predicate)) is an opaque delegate to EF, so only a
    // lambda written in place can translate.
    private static bool HasNonLambdaDelegateArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null || GetDelegateParameterCount(argument.Parameter.Type) < 0)
                continue;

            var value = argument.Value;
            while (value is IConversionOperation or IDelegateCreationOperation)
                value = value is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)value).Target;

            if (value is not IAnonymousFunctionOperation)
                return true;
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
    // scalar result, not on the group's elements. A sub-sequence or a materialized g.ToList() is not a
    // scalar: a call over it, as in g.ToList().Where(p), runs over the group's rows.
    private static bool ReferencesGroupOnlyThroughAggregates(IInvocationOperation invocation, IParameterSymbol groupParam, bool groupProjections)
    {
        var aggregates = GetAllOperations(invocation)
            .OfType<IInvocationOperation>()
            .Where(candidate => !ReferenceEquals(candidate, invocation) &&
                                !ReturnsSequence(candidate.Type) &&
                                IsTranslatableGroupAccess(candidate, groupParam, groupProjections))
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

    // A bare group sub-sequence (g.Where(p), g.ToList()) still carries the group's rows, so a call
    // over it is not shielded; only scalar and element results are.
    private static bool ReturnsSequence(ITypeSymbol? type)
    {
        if (type == null || type.SpecialType == SpecialType.System_String)
            return false;

        return type.SpecialType == SpecialType.System_Collections_IEnumerable ||
               type.AllInterfaces.Any(candidate => candidate.SpecialType == SpecialType.System_Collections_IEnumerable);
    }

    private static bool RootsAtGroupParam(IOperation? receiver, IParameterSymbol groupParam, bool groupProjections)
    {
        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is IParameterReferenceOperation parameterReference)
                return SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, groupParam);

            // A materializer or element accessor ends the translated group query, so it is never a link
            // in the middle: in g.ToList().Where(p) the Where runs over the materialized list.
            if (current is IInvocationOperation chained && IsGroupChainMethod(chained.TargetMethod, groupProjections: false))
            {
                current = chained.GetInvocationReceiver();
                continue;
            }

            return false;
        }

        return false;
    }

    private static IInvocationOperation FindOutermostGroupChainInvocation(IInvocationOperation invocation, bool groupProjections)
    {
        var outermost = invocation;
        while (true)
        {
            if (groupProjections && IsGroupProjectionTerminal(outermost.TargetMethod))
                return outermost;

            IOperation? parent = outermost.Parent;
            while (parent is IConversionOperation or IArgumentOperation)
                parent = parent.Parent;

            if (parent is IInvocationOperation parentInvocation &&
                IsGroupChainMethod(parentInvocation.TargetMethod, groupProjections) &&
                ReferenceEquals(parentInvocation.GetInvocationReceiver()?.UnwrapConversions(), outermost))
            {
                outermost = parentInvocation;
                continue;
            }

            return outermost;
        }
    }

    private static bool IsGroupChainMethod(IMethodSymbol method, bool groupProjections)
    {
        return (IsAllowedAggregateMethod(method.Name) || IsTranslatableGroupOperator(method) ||
                groupProjections && IsGroupProjectionTerminal(method)) &&
               IsKnownAggregateContainingType(method.ContainingType);
    }

    // Only the plain overloads translate: Distinct() without a comparer, and Where/Select/OrderBy/ThenBy
    // with a single one-parameter lambda. Indexed predicates and comparer overloads stay reported.
    private static bool IsTranslatableGroupOperator(IMethodSymbol method)
    {
        if (!TranslatableGroupOperators.Contains(method.Name))
            return false;

        var parameters = (method.IsExtensionMethod && method.ReducedFrom == null
            ? method.Parameters.Skip(1)
            : method.Parameters).ToList();
        if (method.Name == "Distinct")
            return parameters.Count == 0;

        return parameters.Count == 1 && GetDelegateParameterCount(parameters[0].Type) == 1;
    }

    private static int GetDelegateParameterCount(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expression)
            type = expression.TypeArguments[0];

        return type is INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }
            ? invoke.Parameters.Length
            : -1;
    }

    private static bool IsKnownAggregateContainingType(INamedTypeSymbol? containingType)
    {
        var containingNamespace = containingType?.ContainingNamespace?.ToString();
        return containingNamespace == "System.Linq" && containingType is { Name: "Enumerable" or "Queryable" } ||
               containingNamespace == "Microsoft.EntityFrameworkCore" && containingType?.Name == "EntityFrameworkQueryableExtensions";
    }
}
