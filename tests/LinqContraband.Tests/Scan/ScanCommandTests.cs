using System.Xml.Linq;
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
    public void RuleErrorHint_NamesTheRulesThatFailedTheBuild()
    {
        var output = string.Join(
            "\n",
            "/src/A.cs(3,1): error LC018: Avoid FromSqlRaw with interpolated strings [/src/A.csproj]",
            "/src/B.cs(9,5): error EF1002: Method 'FromSqlRaw' inserts interpolated strings [/src/A.csproj]",
            "/src/C.cs(1,1): error LC018: Avoid FromSqlRaw with interpolated strings [/src/A.csproj]",
            "/src/D.cs(1,1): warning LC007: N+1 [/src/A.csproj]");

        var hint = ScanCommand.RuleErrorHint(output);

        Assert.NotNull(hint);
        Assert.StartsWith("The build failed because EF1002, LC018 are set to error severity", hint);
        Assert.Contains("Set them to warning in .editorconfig", hint);
        Assert.Contains("because LC003 is set", ScanCommand.RuleErrorHint("x.cs(1,1): error LC003: Any [p]"));
        Assert.Null(ScanCommand.RuleErrorHint("x.cs(1,1): error CS1002: ; expected [p]"));
    }

    [Fact]
    public void DisplayPath_IsRelativeInsideTheCurrentDirectoryAndFullOutsideIt()
    {
        var inside = Path.Combine(Environment.CurrentDirectory, "out", "report.sarif");
        var outside = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "report.sarif"));

        Assert.Equal(Path.Combine("out", "report.sarif"), ScanCommand.DisplayPath(inside));
        Assert.Equal(outside, ScanCommand.DisplayPath(outside));
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
        Assert.Contains("-p:LinqContrabandPreset=", arguments);
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
}
