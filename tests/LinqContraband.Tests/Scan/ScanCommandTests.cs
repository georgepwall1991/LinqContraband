using System.Text.Json;
using System.Xml.Linq;
using LinqContraband.Catalog;
using LinqContraband.Scan;

namespace LinqContraband.Tests.Scan;

public sealed class ScanCommandTests
{
    private static (int ExitCode, string Output, string Error) Run(string[] args, string? analyzerPath = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ScanCommand.Run(args, output, error, analyzerPath);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (exitCode, output, _) = Run(["--help"]);
        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.Contains("Usage: linqcontraband-scan", output);
    }

    [Fact]
    public void Version_MatchesTheAnalyzerPackage()
    {
        var csproj = XDocument.Load(Path.Combine(Architecture.RepositoryLayout.GetRepositoryRoot(), "src", "LinqContraband", "LinqContraband.csproj"));
        var version = csproj.Descendants("Version").Single().Value;

        var (exitCode, output, _) = Run(["--version"]);

        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.Equal($"linqcontraband-scan {version}", output.Trim());
    }

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        var (exitCode, _, error) = Run(["--nope"]);
        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Unknown option '--nope'.", error);
    }

    [Fact]
    public void MissingPath_IsAUsageError()
    {
        var (exitCode, _, error) = Run([Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))]);
        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Nothing to scan at", error);
    }

    [Fact]
    public void MissingAnalyzer_FailsBeforeBuilding()
    {
        var (exitCode, output, error) = Run([Path.GetTempPath()], analyzerPath: Path.Combine(Path.GetTempPath(), "missing", "LinqContraband.dll"));
        Assert.Equal(ScanCommand.ScanFailed, exitCode);
        Assert.Contains("The LinqContraband analyzer is missing", error);
        Assert.DoesNotContain("Building", output);
    }

    [Fact]
    public void BuildErrorExcerpt_KeepsDistinctErrorLines()
    {
        var output = string.Join(
            "\n",
            "Determining projects to restore...",
            "/src/A.cs(3,1): error CS1002: ; expected [/src/A.csproj]\r",
            "/src/A.cs(3,1): error CS1002: ; expected [/src/A.csproj]",
            "/src/B.cs(1,1): warning CS0168: unused");

        var excerpt = ScanCommand.BuildErrorExcerpt(output);

        Assert.Equal(
            "dotnet build reported errors:" + Environment.NewLine + "  /src/A.cs(3,1): error CS1002: ; expected [/src/A.csproj]",
            excerpt);
        Assert.Contains("--verbose", ScanCommand.BuildErrorExcerpt("Build FAILED."));
    }

    [Fact]
    public void Targets_SwapInTheScannerAnalyzerAndLogEachCompilationSeparately()
    {
        var analyzer = Path.Combine(Path.GetTempPath(), "tool & co", "LinqContraband.dll");
        var logs = Path.Combine(Path.GetTempPath(), "logs");

        var project = XDocument.Parse(ScanTargets.Create(analyzer, logs));

        var target = Assert.Single(project.Root!.Elements("Target"));
        Assert.Equal("CoreCompile", target.Attribute("BeforeTargets")?.Value);
        var analyzers = target.Element("ItemGroup")!.Elements("Analyzer").ToList();
        Assert.Equal("@(Analyzer)", analyzers[0].Attribute("Remove")?.Value);
        Assert.Equal("'%(Filename)' == 'LinqContraband'", analyzers[0].Attribute("Condition")?.Value);
        Assert.Equal(analyzer, analyzers[1].Attribute("Include")?.Value);

        var errorLog = target.Element("PropertyGroup")!.Element("ErrorLog")!.Value;
        Assert.StartsWith(logs + Path.DirectorySeparatorChar + "$(MSBuildProjectName).", errorLog);
        Assert.Contains("$([System.Guid]::NewGuid()", errorLog);
        Assert.EndsWith(".sarif,version=2.1", errorLog);
    }

    [Fact]
    public void BuildArguments_ForceAnalyzersAndRecompileWithoutFailingOnWarnings()
    {
        var options = new ScanOptions { Configuration = "Release", Framework = "net9.0", NoRestore = true };

        var arguments = Scanner.BuildArguments(options, "/repo/App.sln", "/tmp/scan.targets");

        Assert.Equal(["build", "/repo/App.sln", "--no-incremental"], arguments.Take(3));
        Assert.Contains("-p:CustomAfterMicrosoftCommonTargets=/tmp/scan.targets", arguments);
        Assert.Contains("-p:RunAnalyzersDuringBuild=true", arguments);
        Assert.Contains("-p:TreatWarningsAsErrors=false", arguments);
        Assert.Contains("-p:WarningsAsErrors=", arguments);
        Assert.Contains("-p:EnableNETAnalyzers=false", arguments);
        Assert.Equal(["-c", "Release"], arguments.SkipWhile(argument => argument != "-c").Take(2));
        Assert.Equal(["-f", "net9.0"], arguments.SkipWhile(argument => argument != "-f").Take(2));
        Assert.Contains("--no-restore", arguments);
        Assert.Equal("-v:quiet", arguments[^1]);

        var defaults = Scanner.BuildArguments(new ScanOptions { Verbose = true }, "/repo", "/tmp/scan.targets");
        Assert.DoesNotContain("-c", defaults);
        Assert.DoesNotContain("-f", defaults);
        Assert.DoesNotContain("--no-restore", defaults);
        Assert.Equal("-v:normal", defaults[^1]);
    }

    [Fact]
    public void RepositoryRoot_IsTheNearestDirectoryHoldingGit()
    {
        var root = Directory.CreateTempSubdirectory("scan-git-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            Assert.NotEqual(root, Scanner.FindRepositoryRoot(nested));

            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: elsewhere");
            Assert.Equal(root, Scanner.FindRepositoryRoot(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

#if NET10_0_OR_GREATER
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
#endif
}
