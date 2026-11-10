using System;
using System.Reflection;
using NPlusOneBenchmark.Corpus;

namespace NPlusOneBenchmark;

/// <summary>One corpus case: a static class with a <c>Run</c> method and an expectation attribute.</summary>
internal sealed record BenchmarkCase(string Name, Kind Kind, Via Via, bool ExpectedNPlusOne, MethodInfo Run)
{
    public bool UsesLazyLoading => Run.GetParameters()[0].ParameterType == typeof(LazyShopDb);

    public static BenchmarkCase? TryCreate(Type type)
    {
        var nPlusOne = type.GetCustomAttribute<NPlusOneAttribute>();
        var safe = type.GetCustomAttribute<SafeAttribute>();
        if (nPlusOne == null && safe == null)
            return null;

        var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{type.Name} has no public static Run method.");

        var parameters = run.GetParameters();
        if (parameters.Length != 1 ||
            (parameters[0].ParameterType != typeof(ShopDb) && parameters[0].ParameterType != typeof(LazyShopDb)))
        {
            throw new InvalidOperationException($"{type.Name}.Run must take exactly one ShopDb or LazyShopDb.");
        }

        return nPlusOne != null
            ? new BenchmarkCase(type.Name, nPlusOne.Kind, nPlusOne.Via, true, run)
            : new BenchmarkCase(type.Name, safe!.Kind, safe.Via, false, run);
    }
}

/// <summary>Round-trip counts measured for a case at the small and the large data size.</summary>
internal sealed record Measurement(int SmallCount, int LargeCount)
{
    /// <summary>The ground truth: the case is N+1 exactly when more data means more round trips.</summary>
    public bool IsNPlusOne => LargeCount > SmallCount;
}

internal sealed record CaseResult(BenchmarkCase Case, Measurement Measurement, string[] ReportedRules)
{
    public bool Reported => ReportedRules.Length > 0;
}
