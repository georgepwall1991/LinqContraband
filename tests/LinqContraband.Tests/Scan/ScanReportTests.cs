using System.Text;
using System.Text.Json;
using LinqContraband.Scan;

namespace LinqContraband.Tests.Scan;

public sealed class ScanReportTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "scan-root");

    private static string InRoot(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    /// <summary>A SARIF 2.1 error log shaped like the one the C# compiler writes.</summary>
    private static string CompilerLog(params string[] results) => $$"""
        {
          "$schema": "http://json.schemastore.org/sarif-2.1.0",
          "version": "2.1.0",
          "runs": [{
            "results": [{{string.Join(",", results)}}],
            "tool": { "driver": { "name": "Microsoft (R) Visual C# Compiler", "rules": [
              { "id": "CA1822", "shortDescription": { "text": "Mark members as static" } },
              { "id": "LC007", "shortDescription": { "text": "N+1 Problem: Database execution inside loop" },
                "helpUri": "https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html" },
              { "id": "LC031", "shortDescription": { "text": "Unbounded Query Materialization" },
                "defaultConfiguration": { "level": "note" },
                "helpUri": "https://georgepwall1991.github.io/LinqContraband/LC031_UnboundedQueryMaterialization.html" }
            ] } }
          }]
        }
        """;

    private static string Result(string ruleId, string level, string path, int line, int column, bool suppressed = false) => $$"""
        {
          "ruleId": "{{ruleId}}",
          "level": "{{level}}",
          "message": { "text": "{{ruleId}} message" },
          {{(suppressed ? "\"suppressions\": [{ \"kind\": \"inSource\" }]," : "")}}
          "locations": [{ "physicalLocation": {
            "artifactLocation": { "uri": "{{FileUri(path)}}" },
            "region": { "startLine": {{line}}, "startColumn": {{column}} } } }]
        }
        """;

    private static ScanReport ReadReport(params string[] logs)
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
        foreach (var log in logs)
            SarifReader.Read(log, findings, rules);
        return new ScanReport(Root, findings, rules);
    }

    [Fact]
    public void Reader_KeepsOnlyUnsuppressedLinqContrabandResults()
    {
        var report = ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 12, 9),
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 40, 5, suppressed: true),
            Result("CA1822", "warning", InRoot("src", "Orders.cs"), 3, 1),
            Result("LCPRESET", "warning", InRoot("src", "Orders.cs"), 1, 1)));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(new Finding("LC007", "Warning", "LC007 message", "src/Orders.cs", 12, 9), finding);
        Assert.Equal(["LC007", "LC031"], report.Rules.Keys.Order());
        Assert.Equal("N+1 Problem: Database execution inside loop", report.GetRule("LC007").Title);
        Assert.Equal("Info", report.GetRule("LC031").Severity);
        Assert.Equal("Warning", report.GetRule("LC007").Severity);
    }

    [Fact]
    public void Reader_IgnoresLogsWithoutRunsOrLocations()
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, RuleInfo>();
        SarifReader.Read("{}", findings, rules);
        SarifReader.Read("""{ "runs": [{ "results": [{ "ruleId": "LC001", "level": "warning" }] }] }""", findings, rules);
        Assert.Empty(findings);
        Assert.Empty(rules);
    }

    [Fact]
    public void Findings_ReportedByEveryTargetFramework_AreCountedOnce()
    {
        var net8 = CompilerLog(Result("LC007", "warning", InRoot("Orders.cs"), 12, 9));
        var net9 = CompilerLog(Result("LC007", "warning", InRoot("Orders.cs"), 12, 9), Result("LC007", "warning", InRoot("Orders.cs"), 30, 9));

        var report = ReadReport(net8, net9);

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(2, Assert.Single(report.RuleSummaries()).Count);
    }

    [Fact]
    public void RuleSummaries_PutMoreSevereRulesFirstThenMoreFrequent()
    {
        var report = ReadReport(CompilerLog(
            Result("LC031", "note", InRoot("A.cs"), 1, 1),
            Result("LC031", "note", InRoot("A.cs"), 2, 1),
            Result("LC031", "note", InRoot("A.cs"), 3, 1),
            Result("LC007", "warning", InRoot("B.cs"), 1, 1),
            Result("LC018", "error", InRoot("C.cs"), 1, 1),
            Result("LC002", "warning", InRoot("B.cs"), 2, 1),
            Result("LC002", "warning", InRoot("B.cs"), 3, 1)));

        Assert.Equal(["LC018", "LC002", "LC007", "LC031"], report.RuleSummaries().Select(summary => summary.Rule.Id));
        Assert.Equal(["Error", "Warning", "Warning", "Info"], report.RuleSummaries().Select(summary => summary.Severity));
        Assert.Equal([("A.cs", 3), ("B.cs", 3)], report.TopFiles(2));
    }

    [Fact]
    public void Paths_OutsideTheRoot_StayAbsolute()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Shared.cs");
        Assert.Equal(Path.GetFullPath(outside), ScanReport.ToRelativePath(Root, outside));
        Assert.Equal("src/Deep/File.cs", ScanReport.ToRelativePath(Root, InRoot("src", "Deep", "File.cs")));
    }

    [Fact]
    public void Text_ListsRulesFilesLinksAndTheSarifPath()
    {
        var report = ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 12, 9),
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 20, 9),
            Result("LC031", "note", InRoot("src", "Reports.cs"), 5, 1)));

        var text = report.RenderText(topFiles: 5, sarifPath: "linqcontraband.sarif");

        Assert.Contains("LinqContraband found 3 problems (2 rules, 2 files).", text);
        Assert.Contains("  LC007  Warning      2  N+1 Problem: Database execution inside loop", text);
        Assert.Contains("  LC031  Info         1  Unbounded Query Materialization", text);
        Assert.Contains("      2  src/Orders.cs", text);
        Assert.Contains("  LC007  https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html", text);
        Assert.Contains("SARIF report: linqcontraband.sarif", text);
        Assert.Contains("dotnet add package LinqContraband", text);
        Assert.True(text.IndexOf("LC007  Warning", StringComparison.Ordinal) < text.IndexOf("LC031  Info", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_WithoutFindings_SaysSo()
    {
        var text = ReadReport(CompilerLog()).RenderText(topFiles: 10, sarifPath: null);

        Assert.StartsWith("LinqContraband found no EF Core query problems.", text);
        Assert.DoesNotContain("SARIF report", text);
    }

    [Fact]
    public void Text_WithManyRules_LinksTheCatalogInsteadOfListingEveryRule()
    {
        var results = Enumerable.Range(1, 12).Select(id => Result($"LC{id:000}", "warning", InRoot("A.cs"), id, 1)).ToArray();

        var text = ReadReport(CompilerLog(results)).RenderText(topFiles: 0, sarifPath: null);

        Assert.Contains("  LC010  " + ScanReport.RuleCatalogUri, text);
        Assert.DoesNotContain("  LC011  " + ScanReport.RuleCatalogUri, text);
        Assert.Contains("  Every rule: " + ScanReport.RuleCatalogUri, text);
        Assert.DoesNotContain("Most affected files", text);
    }

    [Fact]
    public void Sarif_HasOneRunWithRelativeLocationsAndMatchingRuleIndexes()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Shared.cs");
        var report = ReadReport(CompilerLog(
            Result("LC031", "note", InRoot("src", "My Reports.cs"), 5, 3),
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 12, 9),
            Result("LC007", "warning", outside, 1, 1)));

        using var stream = new MemoryStream();
        report.WriteSarif(stream, "9.9.9");
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));

        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        var run = Assert.Single(document.RootElement.GetProperty("runs").EnumerateArray());
        var driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("LinqContraband", driver.GetProperty("name").GetString());
        Assert.Equal("9.9.9", driver.GetProperty("version").GetString());

        var rules = driver.GetProperty("rules").EnumerateArray().ToList();
        Assert.Equal(["LC007", "LC031"], rules.Select(rule => rule.GetProperty("id").GetString()));
        Assert.Equal("note", rules[1].GetProperty("defaultConfiguration").GetProperty("level").GetString());

        var root = run.GetProperty("originalUriBaseIds").GetProperty("%SRCROOT%").GetProperty("uri").GetString();
        Assert.Equal(new Uri(Root + Path.DirectorySeparatorChar).AbsoluteUri, root);

        var results = run.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        foreach (var result in results)
        {
            var index = result.GetProperty("ruleIndex").GetInt32();
            Assert.Equal(rules[index].GetProperty("id").GetString(), result.GetProperty("ruleId").GetString());
        }

        var artifacts = results
            .Select(result => result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation"))
            .ToList();
        var relative = artifacts.Single(artifact => artifact.GetProperty("uri").GetString() == "src/My%20Reports.cs");
        Assert.Equal("%SRCROOT%", relative.GetProperty("uriBaseId").GetString());
        var absolute = artifacts.Single(artifact => !artifact.TryGetProperty("uriBaseId", out _));
        Assert.Equal(new Uri(Path.GetFullPath(outside)).AbsoluteUri, absolute.GetProperty("uri").GetString());

        var region = results.Single(result => result.GetProperty("ruleId").GetString() == "LC031")
            .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");
        Assert.Equal(5, region.GetProperty("startLine").GetInt32());
        Assert.Equal(3, region.GetProperty("startColumn").GetInt32());
    }

    [Theory]
    [InlineData("error", "Error")]
    [InlineData("warning", "Warning")]
    [InlineData("note", "Info")]
    [InlineData("none", "Hidden")]
    [InlineData(null, "Warning")]
    public void SarifLevels_MapToRoslynSeverities(string? level, string severity) =>
        Assert.Equal(severity, SarifReader.ToSeverity(level));
}
