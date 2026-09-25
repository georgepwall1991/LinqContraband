#if NET10_0_OR_GREATER
using System.Text.Json;
using LinqContraband.Catalog;
using LinqContraband.Scan;

namespace LinqContraband.Tests.Scan;

/// <summary>
/// Runs a real <c>dotnet build</c>, so it gets the machine to itself: xUnit runs collections that disable
/// parallelization after the parallel ones, instead of competing with thousands of analyzer tests for the CPU.
/// </summary>
[CollectionDefinition(nameof(ScanEndToEndTests), DisableParallelization = true)]
public sealed class ScanEndToEndCollection;

[Collection(nameof(ScanEndToEndTests))]
public sealed class ScanEndToEndTests
{
    private static (int ExitCode, string Output, string Error) Run(string[] args, string? analyzerPath = null, Func<string, string?>? environment = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ScanCommand.Run(args, output, error, analyzerPath, environment ?? (_ => null));
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Builds a real project with the scanner: the injected analyzer reports, a pragma-suppressed finding stays out,
    /// a severity preset does not turn the finding into a build error, and the SARIF report points at the source file.
    /// </summary>
    [Fact]
    public void Scan_BuildsAProjectAndReportsWhatTheAnalyzersFind()
    {
        var directory = Directory.CreateTempSubdirectory("scan-e2e-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
                    <LinqContrabandPreset>strict</LinqContrabandPreset>
                  </PropertyGroup>
                  <!-- The same wiring as the package's build/LinqContraband.targets. -->
                  <ItemGroup Condition="'$(LinqContrabandPreset)' != ''">
                    <EditorConfigFiles Include="strict.globalconfig" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(directory, "strict.globalconfig"), """
                is_global = true
                global_level = -10
                dotnet_diagnostic.LC003.severity = error
                """);
            File.WriteAllText(Path.Combine(directory, "Queries.cs"), """
                using System.Linq;

                public static class Queries
                {
                    public static bool HasAny(IQueryable<int> query) => query.Count() > 0;

                #pragma warning disable LC003
                    public static bool HasAnyQuiet(IQueryable<int> query) => query.Count() > 0;
                #pragma warning restore LC003
                }
                """);

            var sarif = Path.Combine(directory, "out", "report.sarif");

            var (exitCode, output, error) = Run([directory, "--sarif", sarif, "--verbose"], BuiltAnalyzerPath());
            var transcript = output + Environment.NewLine + error;

            Assert.True(exitCode == ScanCommand.Success, transcript);
            Assert.True(output.Contains("LinqContraband found 1 problem (1 rule, 1 file).", StringComparison.Ordinal), transcript);
            Assert.True(output.Contains("  LC003  Warning      1  ", StringComparison.Ordinal), transcript);
            Assert.True(output.Contains("      1  Queries.cs", StringComparison.Ordinal), transcript);

            using var document = JsonDocument.Parse(File.ReadAllText(sarif));
            var result = Assert.Single(document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray());
            Assert.Equal("LC003", result.GetProperty("ruleId").GetString());
            var location = result.GetProperty("locations")[0].GetProperty("physicalLocation");
            Assert.Equal("Queries.cs", location.GetProperty("artifactLocation").GetProperty("uri").GetString());
            Assert.Equal(5, location.GetProperty("region").GetProperty("startLine").GetInt32());

            // --fail-on turns the finding into exit code 1, and --exclude drops it (and the failure) again.
            var failing = Run([directory, "--sarif", sarif, "--no-restore", "--fail-on", "warning"], BuiltAnalyzerPath());
            Assert.True(failing.ExitCode == ScanCommand.FindingsFound, failing.Output + failing.Error);
            Assert.Contains("Failing: 1 finding is warning severity or higher (--fail-on warning).", failing.Error);

            var excluded = Run([directory, "--sarif", sarif, "--no-restore", "--fail-on", "warning", "--exclude", "Queries.cs"], BuiltAnalyzerPath());
            Assert.True(excluded.ExitCode == ScanCommand.Success, excluded.Output + excluded.Error);
            Assert.Contains("LinqContraband found no EF Core query problems.", excluded.Output);
            Assert.Contains("1 finding left out by --rules, --skip-rules or --exclude.", excluded.Output);

            // In GitHub Actions the scan annotates the finding and appends the Markdown report to the job summary.
            var stepSummary = Path.Combine(directory, "out", "step-summary.md");
            var summary = Path.Combine(directory, "out", "summary.md");
            var actions = new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true", ["GITHUB_STEP_SUMMARY"] = stepSummary };
            var html = Path.Combine(directory, "out", "report.html");
            var inActions = Run([directory, "--sarif", sarif, "--no-restore", "--summary", summary, "--html", html], BuiltAnalyzerPath(), name => actions.GetValueOrDefault(name));
            Assert.True(inActions.ExitCode == ScanCommand.Success, inActions.Output + inActions.Error);
            Assert.Contains("::warning file=Queries.cs,line=5,", inActions.Output);
            Assert.StartsWith("## LinqContraband: 1 EF Core query problem (1 rule, 1 file)", File.ReadAllText(stepSummary));
            Assert.Equal(File.ReadAllText(stepSummary), File.ReadAllText(summary));
            Assert.Contains("<span class=\"line hit\"><span class=\"ln\">5</span>    public static bool HasAny", File.ReadAllText(html));

            var optedOut = Run([directory, "--sarif", sarif, "--no-restore", "--no-github"], BuiltAnalyzerPath(), name => actions.GetValueOrDefault(name));
            Assert.DoesNotContain("::warning", optedOut.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A repository that raises every analyzer diagnostic to an error in <c>.editorconfig</c> (as
    /// evolutionary-architecture-by-example's Fitnet does, with <c>TreatWarningsAsErrors</c> on top) no longer fails the
    /// scan build on LinqContraband's own findings, so the project that depends on the one with findings is built and
    /// scanned too. A rule turned off by its own id stays off, and a real compile error still fails the scan.
    /// </summary>
    [Fact]
    public void Scan_DoesNotFailTheBuildWhenTheRepositoryRaisesAllAnalyzerDiagnosticsToErrors()
    {
        var directory = Directory.CreateTempSubdirectory("scan-errors-e2e-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, ".editorconfig"), """
                root = true

                [*.cs]
                dotnet_analyzer_diagnostic.severity = error
                dotnet_analyzer_diagnostic.category-Performance.severity = error

                [**/Legacy/*.cs]
                dotnet_diagnostic.LC003.severity = none
                """);
            Directory.CreateDirectory(Path.Combine(directory, "Lib", "Legacy"));
            File.WriteAllText(Path.Combine(directory, "Lib", "Lib.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <WarningsAsErrors>$(WarningsAsErrors);LC003</WarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            const string queries = """
                using System.Linq;

                public static class Queries
                {
                    public static bool HasAny(IQueryable<int> query) => query.Count() > 0;
                }
                """;
            File.WriteAllText(Path.Combine(directory, "Lib", "Queries.cs"), queries);
            File.WriteAllText(Path.Combine(directory, "Lib", "Legacy", "Old.cs"), queries.Replace("class Queries", "class OldQueries"));
            Directory.CreateDirectory(Path.Combine(directory, "App"));
            File.WriteAllText(Path.Combine(directory, "App", "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Lib/Lib.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(directory, "App", "AppQueries.cs"), queries.Replace("class Queries", "class AppQueries"));

            var project = Path.Combine(directory, "App", "App.csproj");
            var sarif = Path.Combine(directory, "out", "report.sarif");

            var (exitCode, output, error) = Run([project, "--sarif", sarif, "--verbose"], BuiltAnalyzerPath());
            var transcript = output + Environment.NewLine + error;

            Assert.True(exitCode == ScanCommand.Success, transcript);
            Assert.True(output.Contains("LinqContraband found 2 problems (1 rule, 2 files).", StringComparison.Ordinal), transcript);
            Assert.True(output.Contains("  LC003  Warning      2  ", StringComparison.Ordinal), transcript);
            Assert.DoesNotContain("error LC003", transcript);

            // Legacy/Old.cs has the same finding, but its rule is off there by id, and that setting still applies.
            using (var document = JsonDocument.Parse(File.ReadAllText(sarif)))
            {
                var files = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
                    .Select(result => result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString()!.Split('/')[^1])
                    .Order(StringComparer.Ordinal)
                    .ToList();
                Assert.Equal(["AppQueries.cs", "Queries.cs"], files);
            }

            // A compile error is still a failed build.
            File.WriteAllText(Path.Combine(directory, "Lib", "Broken.cs"), "public class Broken { int value = ; }");
            var broken = Run([project, "--sarif", sarif, "--no-restore"], BuiltAnalyzerPath());
            Assert.True(broken.ExitCode == ScanCommand.ScanFailed, broken.Output + broken.Error);
            Assert.Contains(": error CS", broken.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// <c>--fix</c> applies the code fix through dotnet format and then scans what is left; <c>--exclude</c> keeps a
    /// file as it was.
    /// </summary>
    [Fact]
    public void Fix_AppliesTheCodeFixesAndReportsWhatIsLeft()
    {
        var directory = Directory.CreateTempSubdirectory("scan-fix-e2e-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            const string queries = """
                using System.Linq;

                public static class Queries
                {
                    public static bool HasAny(IQueryable<int> query) => query.Count() > 0;
                }
                """;
            var queriesPath = Path.Combine(directory, "Queries.cs");
            var legacyPath = Path.Combine(directory, "Legacy", "Old.cs");
            File.WriteAllText(queriesPath, queries);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, queries.Replace("class Queries", "class OldQueries"));
            var sarif = Path.Combine(directory, "out", "report.sarif");

            var (exitCode, output, error) = Run([directory, "--fix", "--exclude", "Legacy", "--sarif", sarif], BuiltAnalyzerPath());
            var transcript = output + Environment.NewLine + error;

            Assert.True(exitCode == ScanCommand.Success, transcript);
            Assert.Contains("query.Any();", File.ReadAllText(queriesPath));
            Assert.Equal(queries.Replace("class Queries", "class OldQueries"), File.ReadAllText(legacyPath));
            Assert.True(output.Contains("Applied fixes to 1 file. Review them with 'git diff' before you commit:" + Environment.NewLine + "  Queries.cs", StringComparison.Ordinal), transcript);
            Assert.True(output.Contains("Put back 1 file that --exclude leaves out.", StringComparison.Ordinal), transcript);
            Assert.True(output.Contains("LinqContraband found no EF Core query problems.", StringComparison.Ordinal), transcript);

            // Nothing left to fix: the report says so, and the excluded file's finding is still left out.
            var again = Run([directory, "--fix", "--no-restore", "--exclude", "Legacy", "--sarif", sarif], BuiltAnalyzerPath());
            Assert.True(again.ExitCode == ScanCommand.Success, again.Output + again.Error);
            Assert.Contains("No finding had a fix to apply.", again.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The analyzer as the analyzer project built it, which is what the tool package ships. The copy next to the
    /// test assembly can be instrumented by the coverage collector, and shares a folder with a newer Roslyn.
    /// </summary>
    private static string BuiltAnalyzerPath()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = testOutput.Parent!.Name;
        var built = Path.Combine(
            Architecture.RepositoryLayout.GetRepositoryRoot(), "src", "LinqContraband", "bin", configuration, "netstandard2.0", "LinqContraband.dll");
        return File.Exists(built) ? built : typeof(RuleCatalog).Assembly.Location;
    }
}
#endif
