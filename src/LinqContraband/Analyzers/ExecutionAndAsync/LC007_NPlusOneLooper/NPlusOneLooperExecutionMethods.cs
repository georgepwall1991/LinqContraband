using System;
using System.Collections.Generic;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static partial class NPlusOneLooperAnalysis
{
    private static readonly HashSet<string> ImmediateQueryExecutionMethods = new(StringComparer.Ordinal)
    {
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync",
        "First",
        "FirstOrDefault",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "Single",
        "SingleOrDefault",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "Last",
        "LastOrDefault",
        "LastAsync",
        "LastOrDefaultAsync",
        "Count",
        "LongCount",
        "CountAsync",
        "LongCountAsync",
        "Any",
        "All",
        "AnyAsync",
        "AllAsync",
        "Sum",
        "Average",
        "Min",
        "Max",
        "SumAsync",
        "AverageAsync",
        "MinAsync",
        "MaxAsync",
        "ForEachAsync"
    };

    private static readonly HashSet<string> SetBasedExecutorMethods = new(StringComparer.Ordinal)
    {
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync"
    };
}
