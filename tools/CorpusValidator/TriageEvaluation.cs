using System.Text.Json;

namespace LinqContraband.CorpusValidator;

public sealed record CorpusDiagnostic(string Repository, string Project, string Path, string RuleId, int Line)
{
    public string TriageKey => $"{Repository}|{CorpusManifest.NormalizePath(Project)}|{CorpusManifest.NormalizePath(Path)}|{RuleId}";
}

public sealed record CorpusTriageEntry(string Repository, string Project, string Path, string RuleId, string Verdict, string Note)
{
    public string TriageKey => $"{Repository}|{CorpusManifest.NormalizePath(Project)}|{CorpusManifest.NormalizePath(Path)}|{RuleId}";
}

public sealed record CorpusTriageEvaluation(
    IReadOnlyList<CorpusDiagnostic> Untriaged,
    IReadOnlyList<CorpusDiagnostic> TriagedFalsePositives,
    IReadOnlyList<CorpusDiagnostic> TriagedTruePositives,
    IReadOnlyList<CorpusTriageEntry> StaleEntries,
    IReadOnlyList<CorpusTriageEntry> OrphanedEntries)
{
    public bool Passed => Untriaged.Count == 0 && StaleEntries.Count == 0 && OrphanedEntries.Count == 0;
}

public static class CorpusTriage
{
    public const string FalsePositiveVerdict = "false-positive";
    public const string TruePositiveVerdict = "true-positive";

    public static IReadOnlyList<CorpusTriageEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Corpus triage file not found: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var versionElement) || versionElement.GetInt32() != 1)
        {
            throw new InvalidOperationException($"Corpus triage file at {path} must declare version 1.");
        }

        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Corpus triage file at {path} must declare an entries array.");
        }

        var entries = new List<CorpusTriageEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entriesElement.EnumerateArray())
        {
            string ReadRequiredString(string property)
            {
                if (!entry.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                {
                    throw new InvalidOperationException($"Corpus triage entry at {path} is missing required string property '{property}'.");
                }

                return value.GetString()!;
            }

            var parsed = new CorpusTriageEntry(
                ReadRequiredString("repository"),
                ReadRequiredString("project"),
                CorpusManifest.NormalizePath(ReadRequiredString("path")),
                ReadRequiredString("ruleId"),
                ReadRequiredString("verdict"),
                entry.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String ? note.GetString()! : string.Empty);

            if (parsed.Verdict is not (FalsePositiveVerdict or TruePositiveVerdict))
            {
                throw new InvalidOperationException($"Corpus triage entry '{parsed.TriageKey}' declares unknown verdict '{parsed.Verdict}'. Expected '{FalsePositiveVerdict}' or '{TruePositiveVerdict}'.");
            }

            if (!keys.Add(parsed.TriageKey))
            {
                throw new InvalidOperationException($"Corpus triage file at {path} declares duplicate entry '{parsed.TriageKey}'.");
            }

            entries.Add(parsed);
        }

        return entries;
    }

    public static CorpusTriageEvaluation Evaluate(
        IReadOnlyList<CorpusTriageEntry> triage,
        IReadOnlyList<CorpusDiagnostic> observed,
        IReadOnlyList<CorpusManifestEntry> manifest)
    {
        var byKey = triage.ToDictionary(entry => entry.TriageKey, entry => entry, StringComparer.Ordinal);
        var observedKeys = new HashSet<string>(StringComparer.Ordinal);
        var untriaged = new List<CorpusDiagnostic>();
        var falsePositives = new List<CorpusDiagnostic>();
        var truePositives = new List<CorpusDiagnostic>();

        foreach (var diagnostic in observed)
        {
            observedKeys.Add(diagnostic.TriageKey);

            if (!byKey.TryGetValue(diagnostic.TriageKey, out var entry))
            {
                untriaged.Add(diagnostic);
            }
            else if (entry.Verdict == FalsePositiveVerdict)
            {
                falsePositives.Add(diagnostic);
            }
            else
            {
                truePositives.Add(diagnostic);
            }
        }

        var stale = triage.Where(entry => !observedKeys.Contains(entry.TriageKey)).ToList();

        var manifestIndex = manifest
            .SelectMany(repository => repository.Projects.Select(project => $"{repository.Name}|{CorpusManifest.NormalizePath(project)}"))
            .ToHashSet(StringComparer.Ordinal);

        var orphaned = triage
            .Where(entry => !manifestIndex.Contains($"{entry.Repository}|{entry.Project}"))
            .ToList();

        return new CorpusTriageEvaluation(untriaged, falsePositives, truePositives, stale, orphaned);
    }
}
