using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace LinqContraband.CorpusValidator;

public sealed record WorkspaceLoadResult(string Repository, string Project, Compilation Compilation, IReadOnlyList<string> Warnings);

public sealed record AnalyzerRunResult(ImmutableArray<Diagnostic> Diagnostics, IReadOnlyList<string> AnalyzerExceptions);public static class WorkspaceAnalysis
{
    private static bool _msbuildRegistered;

    public static void RegisterMsBuild()
    {
        if (_msbuildRegistered)
        {
            return;
        }

        MSBuildLocator.RegisterDefaults();
        _msbuildRegistered = true;
    }

    public static IReadOnlyList<DiagnosticAnalyzer> LoadAnalyzers()
    {
        var analyzerAssembly = typeof(LinqContraband.Catalog.RuleCatalog).Assembly;

        var analyzers = analyzerAssembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false } &&
                           typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .OrderBy(analyzer => string.Join(",", analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id)), StringComparer.Ordinal)
            .ToList();

        if (analyzers.Count == 0)
        {
            throw new InvalidOperationException("No DiagnosticAnalyzer instances were found in the LinqContraband assembly.");
        }

        return analyzers;
    }

    public static async Task<WorkspaceLoadResult> LoadProjectAsync(CorpusManifestEntry entry, string project, string corpusRoot)
    {
        RegisterMsBuild();

        var projectPath = Path.Combine(corpusRoot, entry.ProjectPath(project));
        if (!File.Exists(projectPath))
        {
            throw new InvalidOperationException($"Corpus project not found: {projectPath}");
        }

        Restore(projectPath);

        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["TreatWarningsAsErrors"] = "false",
            ["WarningsNotAsErrors"] = "NU1901;NU1902;NU1903;NU1904",
            ["NoWarn"] = "NU1901;NU1902;NU1903;NU1904"
        });
        workspace.SkipUnrecognizedProjects = false;
        var loaded = await workspace.OpenProjectAsync(projectPath);

        if (workspace.Diagnostics.Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure))
        {
            var messages = workspace.Diagnostics
                .Where(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal);
            throw new InvalidOperationException($"MSBuildWorkspace failed to load {projectPath}:{Environment.NewLine}{string.Join(Environment.NewLine, messages.Select(message => $"  {message}"))}");
        }

        var compilation = await loaded.GetCompilationAsync() ?? throw new InvalidOperationException($"Failed to get compilation for {projectPath}.");

        var compilationErrors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error &&
                                 diagnostic.Id != "CS8785")
            .ToList();
        if (compilationErrors.Count > 0)
        {
            var sample = compilationErrors
                .Take(5)
                .Select(diagnostic => $"{diagnostic.Id} at {diagnostic.Location.SourceTree?.FilePath ?? "?"}: {diagnostic.GetMessage()}");
            throw new InvalidOperationException($"Corpus project {projectPath} does not compile cleanly; refusing to analyze an incomplete program:{Environment.NewLine}{string.Join(Environment.NewLine, sample.Select(message => $"  {message}"))}");
        }

        var warnings = workspace.Diagnostics
            .Where(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Warning)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        return new WorkspaceLoadResult(entry.Name, CorpusManifest.NormalizePath(project), compilation, warnings);
    }

    public static async Task<AnalyzerRunResult> RunAllAnalyzersAsync(
        IReadOnlyList<DiagnosticAnalyzer> analyzers,
        Compilation compilation,
        TimeSpan timeout)
    {
        var analyzerExceptions = new List<string>();
        using var cancellation = new CancellationTokenSource(timeout);
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (exception, analyzer, diagnostic) => analyzerExceptions.Add($"{analyzer}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}"),
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);
        var withAnalyzers = compilation.WithAnalyzers(analyzers.ToImmutableArray(), options);
        var diagnosticsTask = withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellation.Token);
        var completed = await Task.WhenAny(diagnosticsTask, Task.Delay(timeout));

        if (!ReferenceEquals(completed, diagnosticsTask))
        {
            cancellation.Cancel();
            throw new TimeoutException($"Full analyzer pass did not complete within {timeout}.");
        }

        return new AnalyzerRunResult(await diagnosticsTask, analyzerExceptions);
    }

    public static async Task<CorpusPerfResult> RunSingleAnalyzerTimedAsync(
        DiagnosticAnalyzer analyzer,
        WorkspaceLoadResult loaded,
        TimeSpan timeout)
    {
        var ruleIds = string.Join(",", analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id));
        var stopwatch = Stopwatch.StartNew();
        ImmutableArray<Diagnostic> diagnostics;

        using (var cancellation = new CancellationTokenSource(timeout))
        {
            var withAnalyzers = loaded.Compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
            var diagnosticsTask = withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellation.Token);
            var completed = await Task.WhenAny(diagnosticsTask, Task.Delay(timeout));
            if (!ReferenceEquals(completed, diagnosticsTask))
            {
                cancellation.Cancel();
                throw new TimeoutException($"Analyzer {ruleIds} on {loaded.Repository}/{loaded.Project} did not complete within {timeout}.");
            }

            diagnostics = await diagnosticsTask;
        }

        stopwatch.Stop();

        return new CorpusPerfResult(
            loaded.Repository,
            loaded.Project,
            ruleIds,
            stopwatch.ElapsedMilliseconds,
            diagnostics.Count(diagnostic => diagnostic.Id.StartsWith("LC", StringComparison.Ordinal)));
    }

    private static void Restore(string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{projectPath}\" -v:minimal",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet restore.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(stdoutTask.Result);
            Console.Error.WriteLine(stderrTask.Result);
            throw new InvalidOperationException($"dotnet restore failed for {projectPath} with exit code {process.ExitCode}.");
        }
    }
}
