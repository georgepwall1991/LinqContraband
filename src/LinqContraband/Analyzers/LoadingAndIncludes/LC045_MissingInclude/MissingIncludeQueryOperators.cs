using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Operators that keep the query shaped as "a stream of the root entity". Anything else
    /// (Select, Join, GroupBy, custom extensions, ...) reshapes the result or may add its own
    /// loading behaviour, so the whole query is conservatively out of scope.
    /// </summary>
    private static readonly ImmutableHashSet<string> ShapePreservingQueryableOperators =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "Where",
            "OrderBy",
            "OrderByDescending",
            "ThenBy",
            "ThenByDescending",
            "Skip",
            "Take",
            "Distinct",
            "Reverse",
            "AsQueryable"
        );

    private static readonly ImmutableHashSet<string> ShapePreservingEntityFrameworkOperators =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "AsNoTracking",
            "AsNoTrackingWithIdentityResolution",
            "AsTracking",
            "AsSplitQuery",
            "AsSingleQuery",
            "TagWith",
            "TagWithCallSite",
            "IgnoreAutoIncludes",
            "IgnoreQueryFilters",
            "Include",
            "ThenInclude"
        );

    /// <summary>
    /// Every name any materializer proof below can accept. Checked first because the proofs
    /// resolve well-known symbols and compare containing types before they ever look at the name,
    /// and this runs for every invocation in a file — <c>Console.WriteLine</c> included. Most
    /// files contain no EF code at all, so the cheapest possible rejection is the common path.
    ///
    /// This set must stay the union of the names the three proofs accept; a name missing here is
    /// a materializer that silently stops being detected.
    /// </summary>
    private static readonly ImmutableHashSet<string> MaterializerNames = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "ElementAt",
        "ElementAtAsync",
        "ElementAtOrDefault",
        "ElementAtOrDefaultAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Last",
        "LastAsync",
        "LastOrDefault",
        "LastOrDefaultAsync",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "ToArray",
        "ToArrayAsync",
        "ToHashSet",
        "ToHashSetAsync",
        "ToList",
        "ToListAsync"
    );

    private static bool IsEntityMaterializer(
        IInvocationOperation invocation,
        out bool returnsCollection
    )
    {
        returnsCollection = false;

        var candidate = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (!MaterializerNames.Contains(candidate.Name))
            return false;

        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null)
            return false;

        if (IsExactToHashSetMaterializer(invocation, compilation))
        {
            returnsCollection = true;
            return true;
        }

        if (IsExactQueryableElementAtMaterializer(invocation, compilation))
            return true;

        return IsExactLegacyEntityMaterializer(invocation, compilation, out returnsCollection);
    }

    private static bool IsExactLegacyEntityMaterializer(
        IInvocationOperation invocation,
        Compilation compilation,
        out bool returnsCollection
    )
    {
        returnsCollection = false;
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Parameters.Length == 0)
            return false;

        var enumerable = WellKnownSymbols.For(compilation).Enumerable;
        var queryable = WellKnownSymbols.For(compilation).Queryable;
        var containingType = method.ContainingType.OriginalDefinition;
        var isEnumerable = SymbolEqualityComparer.Default.Equals(containingType, enumerable);
        var isQueryable = SymbolEqualityComparer.Default.Equals(containingType, queryable);
        if (isEnumerable || isQueryable)
        {
            var hasExactSource = isEnumerable
                ? IsIEnumerableSourceParameter(method.Parameters[0], compilation)
                : method.Parameters[0].Type.IsIQueryable();
            if (!hasExactSource)
                return false;

            if (method.Name is "ToList" or "ToArray")
            {
                if (method.Parameters.Length != 1)
                    return false;

                returnsCollection = true;
                return true;
            }

            var isElementMaterializer =
                method.Name
                is "First"
                    or "FirstOrDefault"
                    or "Single"
                    or "SingleOrDefault"
                    or "Last"
                    or "LastOrDefault";
            if (!isElementMaterializer)
                return false;

            var supportsDefaultValue =
                method.Name is "FirstOrDefault" or "SingleOrDefault" or "LastOrDefault";
            return method.Parameters.Length switch
            {
                1 => true,
                2 => IsPredicateParameter(
                    method.Parameters[1],
                    compilation,
                    expressionWrapped: isQueryable
                )
                    || (
                        supportsDefaultValue
                        && IsDefaultValueParameter(method.Parameters[1], method)
                    ),
                3 => supportsDefaultValue
                    && IsPredicateParameter(
                        method.Parameters[1],
                        compilation,
                        expressionWrapped: isQueryable
                    )
                    && IsDefaultValueParameter(method.Parameters[2], method),
                _ => false,
            };
        }

        var entityFramework = WellKnownSymbols.For(compilation).EntityFrameworkQueryableExtensions;
        var cancellationToken = WellKnownSymbols.For(compilation).CancellationToken;
        if (
            !SymbolEqualityComparer.Default.Equals(containingType, entityFramework)
            || method.Parameters.Length == 0
            || !method.Parameters[0].Type.IsIQueryable()
        )
        {
            return false;
        }

        if (method.Name is "ToListAsync" or "ToArrayAsync")
        {
            if (
                method.Parameters.Length > 2
                || (
                    method.Parameters.Length == 2
                    && !SymbolEqualityComparer.Default.Equals(
                        method.Parameters[1].Type,
                        cancellationToken
                    )
                )
            )
            {
                return false;
            }

            returnsCollection = true;
            return true;
        }

        var isAsyncElementMaterializer =
            method.Name
            is "FirstAsync"
                or "FirstOrDefaultAsync"
                or "SingleAsync"
                or "SingleOrDefaultAsync"
                or "LastAsync"
                or "LastOrDefaultAsync";
        if (!isAsyncElementMaterializer)
            return false;

        return method.Parameters.Length switch
        {
            1 => true,
            2 => SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, cancellationToken)
                || IsPredicateParameter(method.Parameters[1], compilation, expressionWrapped: true),
            3 => IsPredicateParameter(method.Parameters[1], compilation, expressionWrapped: true)
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[2].Type,
                    cancellationToken
                ),
            _ => false,
        };
    }

    private static bool IsPredicateParameter(
        IParameterSymbol parameter,
        Compilation compilation,
        bool expressionWrapped
    )
    {
        var type = parameter.Type as INamedTypeSymbol;
        if (type == null)
            return false;

        if (expressionWrapped)
        {
            var expression = WellKnownSymbols.For(compilation).Expression;
            if (
                !SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expression)
                || type.TypeArguments.Length != 1
            )
            {
                return false;
            }

            type = type.TypeArguments[0] as INamedTypeSymbol;
            if (type == null)
                return false;
        }

        var func = WellKnownSymbols.For(compilation).Func;
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, func)
            && type.TypeArguments.Length == 2
            && type.TypeArguments[1].SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsDefaultValueParameter(IParameterSymbol parameter, IMethodSymbol method)
    {
        return SymbolEqualityComparer.Default.Equals(parameter.Type, method.ReturnType);
    }

    private static bool IsExactCollectionElementExtraction(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null || method.Parameters.Length == 0)
            return false;

        var frameworkEnumerable = WellKnownSymbols.For(compilation).Enumerable;
        if (
            frameworkEnumerable == null
            || !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                frameworkEnumerable
            )
            || !SymbolEqualityComparer.Default.Equals(
                method.Parameters[0].Type.OriginalDefinition,
                compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T)
            )
        )
        {
            return false;
        }

        return method.Name switch
        {
            "First"
            or "FirstOrDefault"
            or "Single"
            or "SingleOrDefault"
            or "Last"
            or "LastOrDefault" => method.Parameters.Length == 1
                || (
                    // The filtered overload still returns one of the sequence's own instances.
                    // The default-value overloads take TSource instead of a predicate and can
                    // hand back an entity the query never produced, so they stay excluded.
                    method.Parameters.Length == 2
                    && IsPredicateParameter(
                        method.Parameters[1],
                        compilation,
                        expressionWrapped: false
                    )
                    && HasInlineEffectFreeCallback(invocation, callbackOrdinal: 1)
                ),
            // MinBy/MaxBy choose an element by key; the three-parameter comparer overloads are
            // excluded by the inline-callback requirement.
            "MinBy" or "MaxBy" => method.Parameters.Length == 2
                && HasInlineEffectFreeCallback(invocation, callbackOrdinal: 1),
            "ElementAt" or "ElementAtOrDefault" => method.Parameters.Length == 2
                && method.Parameters[1].Type.SpecialType == SpecialType.System_Int32,
            _ => false,
        };
    }

    /// <summary>
    /// True when the argument at <paramref name="callbackOrdinal"/> is a single-parameter inline
    /// lambda that cannot itself have loaded the navigation. A method group or a captured
    /// delegate could, so it stays a boundary.
    /// </summary>
    private static bool HasInlineEffectFreeCallback(
        IInvocationOperation invocation,
        int callbackOrdinal
    )
    {
        var callbackValue = invocation
            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == callbackOrdinal)
            ?.Value;
        return TryGetInlineAnonymousFunction(callbackValue) is { } callback
            && callback.Symbol.Parameters.Length == 1
            && IsEffectFreeCallback(callback);
    }
}
