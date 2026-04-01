using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static readonly ImmutableHashSet<string> AllowedQuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "Include",
        "ThenInclude",
        "IgnoreQueryFilters",
        "AsSplitQuery",
        "AsSingleQuery",
        "AsTracking",
        "IgnoreAutoIncludes",
        "TagWith",
        "TagWithCallSite"
    );

    private static readonly ImmutableHashSet<string> MaterializerSteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync"
    );

    private static bool TryAnalyzeLoop(
        IForEachLoopOperation loop,
        ILocalSymbol dbContextLocal,
        out string entityTypeName)
    {
        entityTypeName = "entity";

        if (loop.IsAsynchronous || loop.Locals.Length != 1)
            return false;

        var iterationLocal = loop.Locals[0];
        entityTypeName = iterationLocal.Type.Name;

        if (!TryResolveTrackedQuerySource(loop.Collection, loop, dbContextLocal))
            return false;

        return HasOnlyDirectScalarAssignments(loop.Body, iterationLocal);
    }

    private static bool TryResolveTrackedQuerySource(
        IOperation collection,
        IForEachLoopOperation loop,
        ILocalSymbol dbContextLocal)
    {
        var current = collection.UnwrapConversions();
        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        while (current != null)
        {
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (IsDbContextSetInvocation(invocation, dbContextLocal))
                        return true;

                    if (MaterializerSteps.Contains(invocation.TargetMethod.Name))
                    {
                        current = invocation.GetInvocationReceiver();
                        continue;
                    }

                    if (AllowedQuerySteps.Contains(invocation.TargetMethod.Name))
                    {
                        current = invocation.GetInvocationReceiver();
                        continue;
                    }

                    return false;

                case IPropertyReferenceOperation propertyReference:
                    return IsMatchingDbSetProperty(propertyReference, dbContextLocal);

                case ILocalReferenceOperation localReference:
                    if (!visitedLocals.Add(localReference.Local))
                        return false;

                    if (localReference.Type.IsIQueryable())
                    {
                        if (!TryGetSingleAssignedLocalValue(localReference.Local, loop, out var queryValue))
                            return false;

                        current = queryValue;
                        continue;
                    }

                    if (!TryGetImmediatePreviousLocalValue(loop, localReference.Local, out var valueOperation))
                        return false;

                    if (!TryGetSingleAssignedLocalValue(localReference.Local, loop, out _))
                        return false;

                    current = valueOperation;
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsMatchingDbSetProperty(IPropertyReferenceOperation propertyReference, ILocalSymbol dbContextLocal)
    {
        return propertyReference.Type.IsDbSet() &&
               propertyReference.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, dbContextLocal);
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation, ILocalSymbol dbContextLocal)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext() &&
               invocation.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, dbContextLocal);
    }
}
