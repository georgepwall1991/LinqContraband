using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization;

public sealed partial class UnboundedQueryMaterializationAnalyzer
{
    /// <summary>
    /// <c>Where(x =&gt; ids.Contains(x.Prop)).GroupBy(x =&gt; x.Prop).Select(g =&gt; new { g.Key, Count = g.Count() })</c>:
    /// one row per id in an in-memory list. The projection may read only <c>g.Key</c> and aggregates over <c>g</c>,
    /// and only row-preserving operators (<c>Where</c>, ordering, query options) may sit between the filter and the
    /// <c>GroupBy</c>.
    /// </summary>
    private static bool IsKeyedGroupAggregateProjection(IInvocationOperation select)
    {
        if (!IsQueryableOperator(select, "Select") ||
            select.Arguments.Length != 2 ||
            !TryGetLambdaBody(select.Arguments[1].Value, out var projection, out var projectionBody) ||
            select.GetInvocationReceiver()?.UnwrapConversions() is not IInvocationOperation groupBy ||
            !IsQueryableOperator(groupBy, "GroupBy") ||
            groupBy.Arguments.Length is not (2 or 3) ||
            !TryGetLambdaBody(groupBy.Arguments[1].Value, out var keySelector, out var keyBody) ||
            keyBody.UnwrapConversions() is not IPropertyReferenceOperation
            {
                Instance: IParameterReferenceOperation keyRow
            } groupingKey ||
            !SymbolEqualityComparer.Default.Equals(keyRow.Parameter, keySelector.Symbol.Parameters[0]) ||
            !ReadsOnlyKeyAndAggregates(projectionBody, projection.Symbol.Parameters[0]))
        {
            return false;
        }

        var current = groupBy.GetInvocationReceiver();
        while (current?.UnwrapConversions() is IInvocationOperation operatorCall)
        {
            if (IsQueryableOperator(operatorCall, "Where") &&
                operatorCall.Arguments.Length == 2 &&
                TryGetLambdaBody(operatorCall.Arguments[1].Value, out var filter, out var filterBody) &&
                FiltersPropertyByInMemoryKeys(filterBody, filter.Symbol.Parameters[0], groupingKey.Property))
            {
                return true;
            }

            if (!IsRowPreservingOperator(operatorCall.TargetMethod))
                return false;

            current = operatorCall.GetInvocationReceiver();
        }

        return false;
    }

    private static bool IsQueryableOperator(IInvocationOperation invocation, string name)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        return method.Name == name &&
               method.ContainingType?.Name == "Queryable" &&
               method.ContainingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
    }

    private static bool IsRowPreservingOperator(IMethodSymbol method)
    {
        return IsLinqOrEfCoreOperator(method) &&
               method.Name is
                   "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or
                   "AsNoTracking" or "AsNoTrackingWithIdentityResolution" or "AsTracking" or
                   "AsSplitQuery" or "AsSingleQuery" or "IgnoreQueryFilters" or "IgnoreAutoIncludes" or
                   "TagWith" or "TagWithCallSite" or "Include" or "ThenInclude";
    }

    private static bool TryGetLambdaBody(IOperation argument, out IAnonymousFunctionOperation lambda, out IOperation body)
    {
        lambda = null!;
        body = null!;

        var value = argument.UnwrapConversions();
        if (value is IDelegateCreationOperation delegateCreation)
            value = delegateCreation.Target;

        if (value is not IAnonymousFunctionOperation { Symbol.Parameters.Length: 1 } function ||
            function.Body.Operations.Length != 1 ||
            function.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } returned })
        {
            return false;
        }

        lambda = function;
        body = returned;
        return true;
    }

    private static bool ReadsOnlyKeyAndAggregates(IOperation projection, IParameterSymbol group)
    {
        foreach (var descendant in projection.DescendantsAndSelf())
        {
            if (descendant is not IParameterReferenceOperation reference ||
                !SymbolEqualityComparer.Default.Equals(reference.Parameter, group))
            {
                continue;
            }

            IOperation use = reference;
            while (use.Parent is IConversionOperation or IArgumentOperation)
                use = use.Parent;

            var allowed = use.Parent switch
            {
                IPropertyReferenceOperation { Property.Name: "Key" } => true,
                IInvocationOperation aggregate => IsAggregateMethod(aggregate.TargetMethod.Name) &&
                                                  aggregate.TargetMethod.Name is not ("ExecuteDelete" or "ExecuteDeleteAsync" or
                                                      "ExecuteUpdate" or "ExecuteUpdateAsync"),
                _ => false
            };

            if (!allowed)
                return false;
        }

        return true;
    }

    private static bool FiltersPropertyByInMemoryKeys(IOperation condition, IParameterSymbol row, IPropertySymbol property)
    {
        condition = condition.UnwrapConversions();

        switch (condition)
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and:
                return FiltersPropertyByInMemoryKeys(and.LeftOperand, row, property) ||
                       FiltersPropertyByInMemoryKeys(and.RightOperand, row, property);

            case IInvocationOperation { TargetMethod.Name: "Contains" } contains:
            {
                IOperation? keys = contains.Instance;
                IOperation? value = contains.Arguments.Length == 1 ? contains.Arguments[0].Value : null;
                if (keys == null && contains.Arguments.Length == 2)
                {
                    keys = contains.Arguments[0].Value;
                    value = contains.Arguments[1].Value;
                }

                return keys != null &&
                       IsCollectionContains(contains, keys) &&
                       value?.UnwrapConversions() is IPropertyReferenceOperation
                       {
                           Instance: IParameterReferenceOperation parameterReference
                       } filtered &&
                       SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, row) &&
                       SymbolEqualityComparer.Default.Equals(filtered.Property, property) &&
                       !ReferencesParameter(keys, row) &&
                       keys.UnwrapConversions().Type?.IsIQueryable() != true;
            }

            default:
                return false;
        }
    }

    // Enumerable/MemoryExtensions.Contains, or an instance Contains on a collection. A [DbFunction] or other custom
    // Contains may match every key.
    private static bool IsCollectionContains(IInvocationOperation contains, IOperation keys)
    {
        var method = contains.TargetMethod;
        if (method.IsStatic || method.ReducedFrom != null)
        {
            var type = (method.ReducedFrom ?? method).ContainingType;
            return type?.ContainingNamespace?.ToDisplayString() is "System.Linq" or "System" &&
                   type.Name is "Enumerable" or "MemoryExtensions";
        }

        return keys.UnwrapConversions().Type is { } keysType && ImplementsGenericEnumerable(keysType);
    }

    private static bool ImplementsGenericEnumerable(ITypeSymbol type)
    {
        if (IsGenericEnumerable(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (IsGenericEnumerable(iface))
                return true;
        }

        return false;
    }

    private static bool IsGenericEnumerable(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IEnumerable", TypeArguments.Length: 1 } named &&
        named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
}
