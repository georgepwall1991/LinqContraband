using System.Security.Cryptography;
using System.Text;
using LinqContraband.CorpusValidator;
using Microsoft.CodeAnalysis;

var repoRoot = FindRepoRoot();
var options = Options.Parse(args);

var manifestPath = options.ManifestPath ?? Path.Combine(repoRoot, "tools", "CorpusValidator", "corpus-manifest.json");
var triagePath = options.TriagePath ?? Path.Combine(repoRoot, "tools", "CorpusValidator", "corpus-triage.json");
var baselinePath = options.BaselinePath ?? Path.Combine(repoRoot, "tools", "CorpusValidator", "perf-baseline.json");
var corpusRoot = options.CorpusRoot ?? Path.Combine(repoRoot, "tools", "CorpusValidator", "corpus");

var manifest = CorpusManifest.Load(manifestPath);
var manifestHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath)));

if (options.Mode is Mode.Prepare or Mode.All)
{
    Console.WriteLine($"Preparing {manifest.Corpus.Count} pinned corpus repositories in {corpusRoot}");
    foreach (var entry in manifest.Corpus)
    {
        GitCorpus.EnsurePinned(entry, corpusRoot);
    }
}

var failures = new List<string>();
var exitCode = 0;

try
{
    if (options.Mode is Mode.Validate or Mode.All)
    {
        exitCode |= RunValidation();
    }

    if (options.Mode is Mode.Perf or Mode.All)
    {
        exitCode |= RunPerf();
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Corpus validation aborted: {exception.Message}");
    return 2;
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Corpus validation failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return Math.Max(exitCode, 1);
}

Console.WriteLine();
Console.WriteLine("Corpus validation passed.");
return exitCode;

int RunValidation()
{
    var analyzers = WorkspaceAnalysis.LoadAnalyzers();
    Console.WriteLine($"[validate] loaded {analyzers.Count} analyzers from the LinqContraband assembly");

    var triage = CorpusTriage.Load(triagePath);
    var observed = new List<CorpusDiagnostic>();

    foreach (var entry in manifest.Corpus)
    {
        foreach (var project in entry.Projects)
        {
            Console.WriteLine($"[validate] {entry.Name}/{project}");
            var loaded = WorkspaceAnalysis.LoadProjectAsync(entry, project, corpusRoot).GetAwaiter().GetResult();
            foreach (var warning in loaded.Warnings)
            {
                Console.WriteLine($"  [workspace warning] {warning}");
            }

            var run = WorkspaceAnalysis.RunAllAnalyzersAsync(analyzers, loaded.Compilation, options.ProjectTimeout).GetAwaiter().GetResult();
            var diagnostics = run.Diagnostics;

            foreach (var analyzerException in run.AnalyzerExceptions)
            {
                failures.Add($"{entry.Name}/{project} analyzer exception: {analyzerException}");
            }

            var checkoutRoot = Path.Combine(corpusRoot, entry.Name);
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Id == "AD0000")
                {
                    failures.Add($"{entry.Name}/{project} produced {diagnostic.Id}: {diagnostic.GetMessage()}");
                    continue;
                }

                if (!diagnostic.Id.StartsWith("LC", StringComparison.Ordinal) || diagnostic.Location.SourceTree is null)
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(checkoutRoot, diagnostic.Location.SourceTree.FilePath);
                var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                observed.Add(new CorpusDiagnostic(entry.Name, CorpusManifest.NormalizePath(project), CorpusManifest.NormalizePath(relativePath), diagnostic.Id, line));
            }

            Console.WriteLine($"  {diagnostics.Count(diagnostic => diagnostic.Id.StartsWith("LC", StringComparison.Ordinal))} LC diagnostics");
        }
    }

    var evaluation = CorpusTriage.Evaluate(triage, observed, manifest.Corpus);

    Console.WriteLine();
    Console.WriteLine($"[validate] observed {observed.Count} LC diagnostics: {evaluation.TriagedFalsePositives.Count} triaged false positives, {evaluation.TriagedTruePositives.Count} triaged true positives, {evaluation.Untriaged.Count} untriaged");

    foreach (var diagnostic in evaluation.Untriaged)
    {
        failures.Add($"untriaged diagnostic {diagnostic.RuleId} at {diagnostic.Repository}/{diagnostic.Path}:{diagnostic.Line} ({diagnostic.Project}) — classify it in corpus-triage.json or fix the rule");
    }

    foreach (var entry in evaluation.StaleEntries)
    {
        failures.Add($"stale triage entry {entry.RuleId} at {entry.Repository}/{entry.Path} ({entry.Project}) no longer reproduces — remove it from corpus-triage.json");
    }

    foreach (var entry in evaluation.OrphanedEntries)
    {
        failures.Add($"triage entry {entry.RuleId} at {entry.Repository}/{entry.Path} references a repository/project that is not in corpus-manifest.json");
    }

    return 0;
}

int RunPerf()
{
    var analyzers = WorkspaceAnalysis.LoadAnalyzers();
    var results = new List<CorpusPerfResult>();

    foreach (var entry in manifest.Corpus)
    {
        foreach (var project in entry.Projects)
        {
            Console.WriteLine($"[perf] {entry.Name}/{project}");
            var loaded = WorkspaceAnalysis.LoadProjectAsync(entry, project, corpusRoot).GetAwaiter().GetResult();

            foreach (var analyzer in analyzers)
            {
                try
                {
                    var result = WorkspaceAnalysis.RunSingleAnalyzerTimedAsync(analyzer, loaded, options.RuleTimeout).GetAwaiter().GetResult();
                    results.Add(result);
                    Console.WriteLine($"  {result.Analyzer}: {result.Milliseconds} ms, {result.Diagnostics} LC diagnostics");
                }
                catch (TimeoutException exception)
                {
                    var ruleIds = string.Join(",", analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id));
                    results.Add(new CorpusPerfResult(entry.Name, CorpusManifest.NormalizePath(project), ruleIds, (long)options.RuleTimeout.TotalMilliseconds, 0));
                    failures.Add($"analyzer exceeded the {options.RuleTimeout.TotalSeconds:0}s hard cap: {exception.Message}");
                }
            }

            CorpusPerfBaseline.Write(baselinePath + ".partial", manifestHash, results);
        }
    }

    var previousBaseline = File.Exists(baselinePath) && !options.UpdateBaseline ? CorpusPerfBaseline.Load(baselinePath) : null;

    if (options.UpdateBaseline)
    {
        CorpusPerfBaseline.Write(baselinePath, manifestHash, results);
        Console.WriteLine($"[perf] wrote perf baseline with {results.Count} entries to {baselinePath}");
        return 0;
    }

    if (previousBaseline is not null)
    {
        var manifestMatches = string.Equals(previousBaseline.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase);
        if (!manifestMatches)
        {
            Console.WriteLine("[perf] baseline was recorded against a different corpus manifest; tolerance budgets are skipped and only hard caps apply");
        }

        foreach (var budgetFailure in CorpusPerfBudget.Evaluate(previousBaseline.Entries, results, options.Tolerance, manifestMatches))
        {
            failures.Add($"perf budget exceeded for {budgetFailure.EntryKey}: baseline {budgetFailure.BaselineMilliseconds} ms, observed {budgetFailure.ObservedMilliseconds} ms, budget {budgetFailure.BudgetMilliseconds} ms");
        }

        foreach (var staleEntry in CorpusPerfBudget.FindStaleBaselineEntries(previousBaseline.Entries, results))
        {
            failures.Add($"stale perf baseline entry {staleEntry} no longer runs — regenerate the baseline with --update-baseline");
        }

        var slowest = results.OrderByDescending(result => result.Milliseconds).Take(5).ToList();
        Console.WriteLine();
        Console.WriteLine("[perf] slowest analyzers:");
        foreach (var result in slowest)
        {
            Console.WriteLine($"  {result.Repository}/{result.Project} {result.Analyzer}: {result.Milliseconds} ms");
        }
    }
    else
    {
        Console.WriteLine("[perf] no committed baseline found; run with --update-baseline to record one");
    }

    return 0;
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "LinqContraband.sln")))
        {
            return current.FullName;
        }

        current = current.Parent!;
    }

    throw new InvalidOperationException("Could not locate LinqContraband.sln from the current working directory.");
}

internal enum Mode
{
    All,
    Prepare,
    Validate,
    Perf
}

internal sealed class Options
{
    public Mode Mode { get; private set; } = Mode.All;

    public string? ManifestPath { get; private set; }

    public string? TriagePath { get; private set; }

    public string? BaselinePath { get; private set; }

    public string? CorpusRoot { get; private set; }

    public TimeSpan ProjectTimeout { get; private set; } = TimeSpan.FromMinutes(10);

    public TimeSpan RuleTimeout { get; private set; } = TimeSpan.FromMinutes(2);

    public double Tolerance { get; private set; } = 1.35;

    public bool UpdateBaseline { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "prepare":
                case "validate":
                case "perf":
                case "all":
                    options.Mode = Enum.Parse<Mode>(arg, ignoreCase: true);
                    break;

                case "--manifest":
                    options.ManifestPath = args[++index];
                    break;

                case "--triage":
                    options.TriagePath = args[++index];
                    break;

                case "--baseline":
                    options.BaselinePath = args[++index];
                    break;

                case "--corpus-root":
                    options.CorpusRoot = args[++index];
                    break;

                case "--project-timeout-seconds":
                    options.ProjectTimeout = TimeSpan.FromSeconds(double.Parse(args[++index]));
                    break;

                case "--rule-timeout-seconds":
                    options.RuleTimeout = TimeSpan.FromSeconds(double.Parse(args[++index]));
                    break;

                case "--tolerance":
                    options.Tolerance = double.Parse(args[++index]);
                    break;

                case "--update-baseline":
                    options.UpdateBaseline = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        return options;
    }
}
