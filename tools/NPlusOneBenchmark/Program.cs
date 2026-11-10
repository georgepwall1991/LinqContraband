using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NPlusOneBenchmark;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var check = args.Contains("--check", StringComparer.Ordinal);
        var write = args.Contains("--write", StringComparer.Ordinal);
        if (check && write)
        {
            Console.Error.WriteLine("Use either --check or --write, not both.");
            return 1;
        }

        var cases = CaseRunner.DiscoverCases();
        Console.WriteLine($"Measuring {cases.Count} cases on SQLite...");

        var measurements = new Dictionary<string, Measurement>(StringComparer.Ordinal);
        var mislabeled = new List<string>();
        foreach (var benchmarkCase in cases)
        {
            var measurement = await CaseRunner.MeasureAsync(benchmarkCase);
            measurements[benchmarkCase.Name] = measurement;
            if (measurement.IsNPlusOne != benchmarkCase.ExpectedNPlusOne)
            {
                mislabeled.Add(
                    $"{benchmarkCase.Name} is labelled {(benchmarkCase.ExpectedNPlusOne ? "N+1" : "safe")} " +
                    $"but made {measurement.SmallCount} then {measurement.LargeCount} round trips.");
            }
        }

        // The labels are claims; the round-trip counts are the proof. A case whose label the database
        // does not bear out is a corpus bug, and nothing is scored until it is fixed.
        if (mislabeled.Count > 0)
        {
            Console.Error.WriteLine("The corpus has cases whose label does not match the measured round trips:");
            foreach (var line in mislabeled)
                Console.Error.WriteLine("  " + line);

            return 2;
        }

        Console.WriteLine("Analyzing the corpus with LinqContraband...");
        var reportedByType = await CorpusAnalysis.AnalyzeAsync();

        var caseNames = cases.Select(benchmarkCase => benchmarkCase.Name).ToHashSet(StringComparer.Ordinal);
        var outsideCases = reportedByType.Keys.Where(name => !caseNames.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (outsideCases.Length > 0)
        {
            // The shared layers are single round trips by construction, so a report there can
            // never be attributed to a case and would silently fall out of the score.
            Console.Error.WriteLine("N+1 rules reported outside any case, in: " + string.Join(", ", outsideCases));
            return 2;
        }

        var results = cases
            .Select(benchmarkCase => new CaseResult(
                benchmarkCase,
                measurements[benchmarkCase.Name],
                reportedByType.TryGetValue(benchmarkCase.Name, out var rules) ? rules : Array.Empty<string>()))
            .ToArray();

        var rendered = Report.Render(results);
        var resultsPath = Path.Combine(FindRepoRoot(), "tools", "NPlusOneBenchmark", "results.md");

        Console.WriteLine();
        Console.WriteLine(Report.Summarize(results));

        if (write)
        {
            File.WriteAllText(resultsPath, rendered);
            Console.WriteLine($"Wrote {resultsPath}");
            return 0;
        }

        if (check)
        {
            var committed = File.Exists(resultsPath) ? NormalizeNewlines(File.ReadAllText(resultsPath)) : "";
            if (!string.Equals(committed, NormalizeNewlines(rendered), StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{resultsPath} is out of date. Run: {Report.WriteCommand}");
                Console.Error.WriteLine("Review the diff: it shows every case whose detection changed.");
                return 1;
            }

            Console.WriteLine("results.md is up to date.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine(rendered);
        return 0;
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LinqContraband.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find LinqContraband.sln above " + AppContext.BaseDirectory);
    }
}
