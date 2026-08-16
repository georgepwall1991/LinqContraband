using System.Text.Json;

namespace LinqContraband.CorpusValidator;

public sealed record CorpusPerfResult(string Repository, string Project, string Analyzer, long Milliseconds, int Diagnostics)
{
    public string EntryKey => $"{Repository}|{Project}|{Analyzer}";
}

public sealed record CorpusPerfBaseline(int Version, string ManifestHash, string GeneratedAtUtc, IReadOnlyList<CorpusPerfResult> Entries)
{
    public static CorpusPerfBaseline Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Perf baseline not found: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var versionElement) || versionElement.GetInt32() != 1)
        {
            throw new InvalidOperationException($"Perf baseline at {path} must declare version 1.");
        }

        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Perf baseline at {path} must declare an entries array.");
        }

        var manifestHash = root.TryGetProperty("manifestHash", out var hashElement) && hashElement.ValueKind == JsonValueKind.String ? hashElement.GetString()! : string.Empty;
        var generatedAt = root.TryGetProperty("generatedAtUtc", out var generatedElement) && generatedElement.ValueKind == JsonValueKind.String ? generatedElement.GetString()! : string.Empty;

        var entries = new List<CorpusPerfResult>();
        foreach (var entry in entriesElement.EnumerateArray())
        {
            entries.Add(new CorpusPerfResult(
                entry.GetProperty("repository").GetString()!,
                entry.GetProperty("project").GetString()!,
                entry.GetProperty("analyzer").GetString()!,
                entry.GetProperty("milliseconds").GetInt64(),
                entry.GetProperty("diagnostics").GetInt32()));
        }

        return new CorpusPerfBaseline(1, manifestHash, generatedAt, entries);
    }

    public static void Write(string path, string manifestHash, IReadOnlyList<CorpusPerfResult> results)
    {
        var payload = new
        {
            version = 1,
            manifestHash,
            generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            entries = results
                .OrderBy(result => result.Repository, StringComparer.Ordinal)
                .ThenBy(result => result.Project, StringComparer.Ordinal)
                .ThenBy(result => result.Analyzer, StringComparer.Ordinal)
                .Select(result => new
                {
                    repository = result.Repository,
                    project = result.Project,
                    analyzer = result.Analyzer,
                    milliseconds = result.Milliseconds,
                    diagnostics = result.Diagnostics
                })
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record CorpusPerfBudgetFailure(string EntryKey, long BaselineMilliseconds, long ObservedMilliseconds, long BudgetMilliseconds, string Reason);

public static class CorpusPerfBudget
{
    public static long ComputeBudgetMilliseconds(long baselineMilliseconds, double tolerance)
    {
        return Math.Max((long)Math.Ceiling(baselineMilliseconds * tolerance), 2_000);
    }

    public static IReadOnlyList<CorpusPerfBudgetFailure> Evaluate(
        IReadOnlyList<CorpusPerfResult> baseline,
        IReadOnlyList<CorpusPerfResult> observed,
        double tolerance,
        bool manifestMatches)
    {
        var failures = new List<CorpusPerfBudgetFailure>();
        if (!manifestMatches)
        {
            return failures;
        }

        var byKey = baseline.ToDictionary(entry => entry.EntryKey, entry => entry.Milliseconds, StringComparer.Ordinal);

        foreach (var result in observed)
        {
            if (!byKey.TryGetValue(result.EntryKey, out var baselineMilliseconds))
            {
                failures.Add(new CorpusPerfBudgetFailure(result.EntryKey, 0, result.Milliseconds, 0, "not budgeted — regenerate the baseline with --update-baseline"));
                continue;
            }

            var budget = ComputeBudgetMilliseconds(baselineMilliseconds, tolerance);
            if (result.Milliseconds > budget)
            {
                failures.Add(new CorpusPerfBudgetFailure(result.EntryKey, baselineMilliseconds, result.Milliseconds, budget, "exceeded tolerance budget"));
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> FindStaleBaselineEntries(
        IReadOnlyList<CorpusPerfResult> baseline,
        IReadOnlyList<CorpusPerfResult> observed)
    {
        var observedKeys = observed.Select(result => result.EntryKey).ToHashSet(StringComparer.Ordinal);
        return baseline
            .Where(entry => !observedKeys.Contains(entry.EntryKey))
            .Select(entry => entry.EntryKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }
}
