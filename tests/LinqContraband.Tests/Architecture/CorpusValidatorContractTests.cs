using System.Security.Cryptography;
using System.Text.Json;
using LinqContraband.CorpusValidator;

namespace LinqContraband.Tests.Architecture;

public class CorpusValidatorContractTests
{
    private static string ToolDirectory => Path.Combine(RepositoryLayout.GetRepositoryRoot(), "tools", "CorpusValidator");

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"linqcontraband-corpus-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Manifest_CommittedManifest_ParsesWithPinnedCorpus()
    {
        var manifest = CorpusManifest.Load(Path.Combine(ToolDirectory, "corpus-manifest.json"));

        Assert.True(manifest.Version == 1);
        Assert.True(manifest.Corpus.Count >= 2);

        foreach (var entry in manifest.Corpus)
        {
            Assert.Matches(@"^[0-9a-f]{40}$", entry.Commit);
            Assert.All(entry.Projects, project => Assert.EndsWith(".csproj", project, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Manifest_TracksRepositories_AndTriageReferencesOnlyKnownProjects()
    {
        var manifest = CorpusManifest.Load(Path.Combine(ToolDirectory, "corpus-manifest.json"));
        var triage = CorpusTriage.Load(Path.Combine(ToolDirectory, "corpus-triage.json"));
        var repositoryNames = manifest.Corpus.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(triage, entry => Assert.Contains(entry.Repository, repositoryNames));
    }

    [Fact]
    public void Baseline_ManifestHash_MatchesCommittedManifest()
    {
        var baseline = CorpusPerfBaseline.Load(Path.Combine(ToolDirectory, "perf-baseline.json"));
        var manifestHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(ToolDirectory, "corpus-manifest.json"))));

        Assert.Equal(manifestHash, baseline.ManifestHash, ignoreCase: true);
        Assert.NotEmpty(baseline.Entries);
    }

    [Fact]
    public void GitIgnore_ExcludesCorpusCheckouts()
    {
        var gitignore = File.ReadAllLines(Path.Combine(RepositoryLayout.GetRepositoryRoot(), ".gitignore"));

        Assert.Contains("tools/CorpusValidator/corpus/", gitignore, StringComparer.Ordinal);
    }

    [Fact]
    public void Manifest_RejectsDuplicateCorpusNames()
    {
        var path = TempFile("""{"version":1,"corpus":[{"name":"a","repository":"https://github.com/x/a.git","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","projects":["src/a.csproj"]},{"name":"a","repository":"https://github.com/x/b.git","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","projects":["src/b.csproj"]}]}""");

        Assert.Throws<InvalidOperationException>(() => CorpusManifest.Load(path));
    }

    [Fact]
    public void Manifest_RejectsUnpinnedCommit()
    {
        var path = TempFile("""{"version":1,"corpus":[{"name":"a","repository":"https://github.com/x/a.git","commit":"main","projects":["src/a.csproj"]}]}""");

        Assert.Throws<InvalidOperationException>(() => CorpusManifest.Load(path));
    }

    [Fact]
    public void Manifest_RejectsRootedProjectPath()
    {
        var path = TempFile("""{"version":1,"corpus":[{"name":"a","repository":"https://github.com/x/a.git","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","projects":["/etc/passwd.csproj"]}]}""");

        Assert.Throws<InvalidOperationException>(() => CorpusManifest.Load(path));
    }

    [Fact]
    public void Triage_PassesWhenEveryObservedDiagnosticIsTriaged()
    {
        var manifest = new[] { new CorpusManifestEntry("repo", "https://github.com/x/r.git", new string('a', 40), new[] { "src/App.csproj" }) };
        var triage = new[] { new CorpusTriageEntry("repo", "src/App.csproj", "Data/Seed.cs", "LC039", "true-positive", "") };
        var observed = new[] { new CorpusDiagnostic("repo", "src/App.csproj", "Data/Seed.cs", "LC039", 37) };

        var evaluation = CorpusTriage.Evaluate(triage, observed, manifest);

        Assert.True(evaluation.Passed);
        Assert.Single(evaluation.TriagedTruePositives);
    }

    [Fact]
    public void Triage_FailsOnUntriagedDiagnostic()
    {
        var manifest = new[] { new CorpusManifestEntry("repo", "https://github.com/x/r.git", new string('a', 40), new[] { "src/App.csproj" }) };
        var observed = new[] { new CorpusDiagnostic("repo", "src/App.csproj", "Data/Seed.cs", "LC039", 37) };

        var evaluation = CorpusTriage.Evaluate(Array.Empty<CorpusTriageEntry>(), observed, manifest);

        Assert.False(evaluation.Passed);
        Assert.Single(evaluation.Untriaged);
    }

    [Fact]
    public void Triage_FailsOnStaleEntry()
    {
        var manifest = new[] { new CorpusManifestEntry("repo", "https://github.com/x/r.git", new string('a', 40), new[] { "src/App.csproj" }) };
        var triage = new[] { new CorpusTriageEntry("repo", "src/App.csproj", "Data/Seed.cs", "LC039", "true-positive", "") };

        var evaluation = CorpusTriage.Evaluate(triage, Array.Empty<CorpusDiagnostic>(), manifest);

        Assert.False(evaluation.Passed);
        Assert.Single(evaluation.StaleEntries);
    }

    [Fact]
    public void Triage_FailsOnEntryOutsideManifest()
    {
        var manifest = new[] { new CorpusManifestEntry("repo", "https://github.com/x/r.git", new string('a', 40), new[] { "src/App.csproj" }) };
        var triage = new[] { new CorpusTriageEntry("other", "src/App.csproj", "Data/Seed.cs", "LC039", "true-positive", "") };

        var evaluation = CorpusTriage.Evaluate(triage, Array.Empty<CorpusDiagnostic>(), manifest);

        Assert.False(evaluation.Passed);
        Assert.Single(evaluation.OrphanedEntries);
    }

    [Fact]
    public void Triage_NormalizesPathSeparators()
    {
        var manifest = new[] { new CorpusManifestEntry("repo", "https://github.com/x/r.git", new string('a', 40), new[] { "src/App.csproj" }) };
        var triage = new[] { new CorpusTriageEntry("repo", "src/App.csproj", "Data\\Seed.cs", "LC039", "true-positive", "") };
        var observed = new[] { new CorpusDiagnostic("repo", "src/App.csproj", "Data/Seed.cs", "LC039", 37) };

        var evaluation = CorpusTriage.Evaluate(triage, observed, manifest);

        Assert.True(evaluation.Passed);
    }

    [Fact]
    public void Triage_RejectsUnknownVerdict()
    {
        var path = TempFile("""{"version":1,"entries":[{"repository":"r","project":"p","path":"a.cs","ruleId":"LC001","verdict":"maybe"}]}""");

        Assert.Throws<InvalidOperationException>(() => CorpusTriage.Load(path));
    }

    [Fact]
    public void PerfBudget_AppliesToleranceWithAbsoluteFloor()
    {
        Assert.Equal(2_000, CorpusPerfBudget.ComputeBudgetMilliseconds(1_000, 1.35));
        Assert.Equal(2_000, CorpusPerfBudget.ComputeBudgetMilliseconds(100, 1.35));
    }

    [Fact]
    public void PerfBudget_FailsWhenObservedExceedsBudget()
    {
        var baseline = new[] { new CorpusPerfResult("repo", "proj", "LC001", 1_000, 0) };
        var observed = new[] { new CorpusPerfResult("repo", "proj", "LC001", 3_000, 0) };

        var failures = CorpusPerfBudget.Evaluate(baseline, observed, tolerance: 1.35, manifestMatches: true);

        Assert.Single(failures);
        Assert.Equal(2_000, failures[0].BudgetMilliseconds);
    }

    [Fact]
    public void PerfBudget_PassesWhenWithinBudget()
    {
        var baseline = new[] { new CorpusPerfResult("repo", "proj", "LC001", 1_000, 0) };
        var observed = new[] { new CorpusPerfResult("repo", "proj", "LC001", 1_900, 0) };

        var failures = CorpusPerfBudget.Evaluate(baseline, observed, tolerance: 1.35, manifestMatches: true);

        Assert.Empty(failures);
    }

    [Fact]
    public void PerfBudget_SkipsToleranceWhenManifestChanged()
    {
        var baseline = new[] { new CorpusPerfResult("repo", "proj", "LC001", 1_000, 0) };
        var observed = new[] { new CorpusPerfResult("repo", "proj", "LC001", 60_000, 0) };

        var failures = CorpusPerfBudget.Evaluate(baseline, observed, tolerance: 1.35, manifestMatches: false);

        Assert.Empty(failures);
    }

    [Fact]
    public void PerfBudget_TreatsNewAnalyzerAsUnbudgeted()
    {
        var baseline = new[] { new CorpusPerfResult("repo", "proj", "LC001", 1_000, 0) };
        var observed = new[] { new CorpusPerfResult("repo", "proj", "LC999", 60_000, 0) };

        var failures = CorpusPerfBudget.Evaluate(baseline, observed, tolerance: 1.35, manifestMatches: true);

        Assert.Empty(failures);
    }

    [Fact]
    public void PerfBaseline_RoundTripsThroughJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linqcontraband-baseline-{Guid.NewGuid():N}.json");
        var results = new[] { new CorpusPerfResult("repo", "proj", "LC001", 123, 2) };

        try
        {
            CorpusPerfBaseline.Write(path, "hash", results);
            var loaded = CorpusPerfBaseline.Load(path);

            Assert.Equal("hash", loaded.ManifestHash);
            var entry = Assert.Single(loaded.Entries);
            Assert.Equal(123, entry.Milliseconds);
            Assert.Equal(2, entry.Diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
