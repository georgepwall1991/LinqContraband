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
    private static (int ExitCode, string Output, string Error) Run(string[] args, string? analyzerPath = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ScanCommand.Run(args, output, error, analyzerPath);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Builds a real project with the scanner: the injected analyzer reports, a pragma-suppressed finding stays out,
    /// and the SARIF report points at the source file.
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
                  </PropertyGroup>
                </Project>
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
