using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NPlusOneBenchmark.Corpus;

namespace NPlusOneBenchmark;

/// <summary>Runs corpus cases against a fresh in-memory SQLite database and counts the round trips they make.</summary>
internal static class CaseRunner
{
    public const int SmallSize = 3;
    public const int LargeSize = 6;

    public static IReadOnlyList<BenchmarkCase> DiscoverCases() =>
        typeof(CaseRunner).Assembly.GetTypes()
            .Select(BenchmarkCase.TryCreate)
            .Where(benchmarkCase => benchmarkCase != null)
            .Select(benchmarkCase => benchmarkCase!)
            .OrderBy(benchmarkCase => benchmarkCase.Name, StringComparer.Ordinal)
            .ToArray();

    public static async Task<Measurement> MeasureAsync(BenchmarkCase benchmarkCase) =>
        new Measurement(
            await CountRoundTripsAsync(benchmarkCase, SmallSize),
            await CountRoundTripsAsync(benchmarkCase, LargeSize));

    private static async Task<int> CountRoundTripsAsync(BenchmarkCase benchmarkCase, int size)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var counter = new SqlCounter();
        using (var seedContext = new ShopDb(connection, counter))
        {
            seedContext.Database.EnsureCreated();
            Seed.Fill(seedContext, size);
        }

        // A new context, so nothing the seed tracked can answer a query from memory.
        using ShopDb db = benchmarkCase.UsesLazyLoading
            ? new LazyShopDb(connection, counter)
            : new ShopDb(connection, counter);

        counter.Reset();
        try
        {
            if (benchmarkCase.Run.Invoke(null, new object[] { db }) is Task task)
                await task;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException(
                $"{benchmarkCase.Name} threw at size {size}: {exception.InnerException.Message}",
                exception.InnerException);
        }

        return counter.Count;
    }
}
