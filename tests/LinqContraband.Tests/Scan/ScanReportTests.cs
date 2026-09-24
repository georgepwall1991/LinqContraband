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
              { "id": "EF1002", "shortDescription": { "text": "Risk of vulnerability to SQL injection." } },
              { "id": "LC007", "shortDescription": { "text": "N+1 Problem: Database execution inside loop" },
                "fullDescription": { "text": "Queries inside loops run once per item." },
                "properties": { "category": "Performance" },
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
        Assert.Equal(["EF1002", "LC007", "LC031"], report.Rules.Keys.Order());
        Assert.Equal("N+1 Problem: Database execution inside loop", report.GetRule("LC007").Title);
        Assert.Equal("Info", report.GetRule("LC031").Severity);
        Assert.Equal("Warning", report.GetRule("LC007").Severity);
    }

    [Fact]
    public void Reader_KeepsEfCoreSqlInjectionFindings_ThatLC018AndLC034DeferTo()
    {
        var report = ReadReport(CompilerLog(
            Result("EF1002", "warning", InRoot("Orders.cs"), 7, 60),
            Result("EF1001", "warning", InRoot("Orders.cs"), 9, 1)));

        var finding = Assert.Single(report.Findings);
        Assert.Equal("EF1002", finding.RuleId);
        Assert.Equal("Risk of vulnerability to SQL injection.", report.GetRule("EF1002").Title);
        Assert.Equal(SarifReader.EfCoreSqlQueriesUri, report.GetRule("EF1002").HelpUri);
        Assert.Contains("  EF1002  " + SarifReader.EfCoreSqlQueriesUri, report.RenderText(topFiles: 0, sarifPath: null));
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
    public void Text_ListsEachRulesFindingsWithTheLineOfCode()
    {
        var root = Directory.CreateTempSubdirectory("scan-report-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllLines(Path.Combine(root, "src", "Orders.cs"), [
                "class Orders",
                "{",
                "    void Load() { foreach (var id in ids) db.Orders.Find(id); }",
                "}",
            ]);
            var results = new[]
            {
                Result("LC007", "warning", Path.Combine(root, "src", "Orders.cs"), 3, 43),
                Result("LC007", "warning", Path.Combine(root, "src", "Missing.cs"), 8, 5),
                Result("LC007", "warning", Path.Combine(root, "src", "Orders.cs"), 40, 1),
                Result("LC031", "note", Path.Combine(root, "src", "Orders.cs"), 1, 1),
            };
            var findings = new List<Finding>();
            var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
            SarifReader.Read(CompilerLog(results), findings, rules);
            var report = new ScanReport(root, findings, rules);

            var text = report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: 2);

            Assert.Contains("""
                Findings:

                  LC007  N+1 Problem: Database execution inside loop
                    src/Missing.cs:8:5
                      LC007 message
                    src/Orders.cs:3:43
                      LC007 message
                      3 | void Load() { foreach (var id in ids) db.Orders.Find(id); }
                    ...and 1 more in the SARIF report.

                  LC031  Unbounded Query Materialization
                    src/Orders.cs:1:1
                      LC031 message
                      1 | class Orders
                """.ReplaceLineEndings(), text);
            Assert.DoesNotContain("Findings:", report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: 0));
            Assert.DoesNotContain("more in the SARIF report", report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: int.MaxValue));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("**/Migrations/**", "src/Data/Migrations/2024_Init.cs", true)]
    [InlineData("Migrations", "src/Data/Migrations/2024_Init.cs", true)]
    [InlineData("Migrations", "src/Data/MigrationsHelper.cs", false)]
    [InlineData("tests/**", "tests/App.Tests/OrderTests.cs", true)]
    [InlineData("tests/**", "src/tests.cs", false)]
    [InlineData("tests", "src/App/tests/Seed.cs", true)]
    [InlineData("*.Designer.cs", "src/Data/Model.Designer.cs", true)]
    [InlineData("src/*.cs", "src/Orders.cs", true)]
    [InlineData("src/*.cs", "src/Orders/Service.cs", false)]
    [InlineData("./src/Orders?.cs", "src/Orders2.cs", true)]
    [InlineData("src\\Legacy", "src/Legacy/Old.cs", true)]
    public void ExcludeGlobs_MatchRelativePaths(string glob, string path, bool expected)
    {
        Assert.Equal(expected, ScanFilter.GlobToRegex(glob).IsMatch(path));
    }

    [Fact]
    public void Filter_KeepsTheSelectedRulesAndFilesAndCountsTheRest()
    {
        var report = ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 12, 9),
            Result("LC007", "warning", InRoot("src", "Migrations", "Init.cs"), 3, 1),
            Result("LC031", "note", InRoot("src", "Orders.cs"), 20, 1),
            Result("EF1002", "warning", InRoot("src", "Orders.cs"), 30, 1)));

        var filtered = report
            .Filter(new ScanFilter(["LC007", "lc031"], [], ["Migrations"]))
            .Filter(new ScanFilter([], ["LC031"], []));

        var finding = Assert.Single(filtered.Findings);
        Assert.Equal(("LC007", "src/Orders.cs"), (finding.RuleId, finding.Path));
        Assert.Equal(3, filtered.FilteredOut);
        Assert.Contains("3 more findings left out by --rules, --skip-rules or --exclude.", filtered.RenderText(topFiles: 0, sarifPath: null));
        Assert.Same(report, report.Filter(new ScanFilter([], [], [])));
    }

    [Fact]
    public void CountAtLeast_CountsFindingsAtOrAboveASeverity()
    {
        var report = ReadReport(CompilerLog(
            Result("LC007", "error", InRoot("A.cs"), 1, 1),
            Result("LC007", "warning", InRoot("A.cs"), 2, 1),
            Result("LC031", "note", InRoot("A.cs"), 3, 1)));

        Assert.Equal(1, report.CountAtLeast("Error"));
        Assert.Equal(2, report.CountAtLeast("Warning"));
        Assert.Equal(3, report.CountAtLeast("Info"));
    }

    private static ScanReport ReportIn(string root, params string[] results)
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
        SarifReader.Read(CompilerLog(results), findings, rules);
        return new ScanReport(root, findings, rules);
    }

    private static string SarifText(ScanReport report)
    {
        using var stream = new MemoryStream();
        report.WriteSarif(stream, "9.9.9");
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Baseline_MatchesFindingsThatMovedAndReportsOnlyNewOnes()
    {
        var root = Directory.CreateTempSubdirectory("scan-baseline-").FullName;
        try
        {
            var file = Path.Combine(root, "Orders.cs");
            File.WriteAllLines(file, ["class Orders {", "  void A() => db.Orders.ToList();", "  void B() => db.Orders.ToList();", "}"]);
            var before = ReportIn(root,
                Result("LC031", "note", file, 2, 15),
                Result("LC031", "note", file, 3, 15));
            var baseline = ScanBaseline.Parse(SarifText(before));
            Assert.Equal(2, baseline.Count);
            Assert.NotEqual(before.Fingerprint(before.Findings[0]), before.Fingerprint(before.Findings[1]));

            // Two lines inserted above, indentation changed, and a third identical call added.
            File.WriteAllLines(file, ["using System.Linq;", "", "class Orders {", "    void A() => db.Orders.ToList();", "    void B() => db.Orders.ToList();", "    void C() => db.Orders.ToList();", "}"]);
            var after = ReportIn(root,
                Result("LC031", "note", file, 4, 17),
                Result("LC031", "note", file, 5, 17),
                Result("LC031", "note", file, 6, 17),
                Result("LC007", "warning", file, 6, 17));

            var report = after.ApplyBaseline(baseline);

            Assert.Equal([("LC031", 6), ("LC007", 6)], report.Findings.Select(finding => (finding.RuleId, finding.Line)).OrderByDescending(pair => pair.RuleId));
            Assert.Equal([4, 5], report.BaselineFindings.Select(finding => finding.Line));
            Assert.Equal(1, report.CountAtLeast("Warning"));

            var text = report.RenderText(topFiles: 0, sarifPath: null);
            Assert.Contains("LinqContraband found 2 new problems (2 rules, 1 file).", text);
            Assert.Contains("2 findings already in the baseline are not listed; the SARIF report keeps them.", text);

            using var document = JsonDocument.Parse(SarifText(report));
            var results = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToList();
            Assert.Equal(4, results.Count);
            Assert.Equal(
                ["unchanged", "unchanged", "new", "new"],
                results.Select(result => result.GetProperty("baselineState").GetString()));
            Assert.All(results, result => Assert.Equal(32, result.GetProperty("partialFingerprints").GetProperty(ScanReport.FingerprintKey).GetString()!.Length));

            Assert.Contains("found no new EF Core query problems", before.ApplyBaseline(baseline).RenderText(topFiles: 0, sarifPath: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Baseline_WithoutFingerprints_MatchesOnRuleFileAndMessage()
    {
        var baseline = ScanBaseline.Parse("""
            { "runs": [{ "results": [
              { "ruleId": "LC007", "message": { "text": "LC007 message" },
                "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "src/My%20Orders.cs", "uriBaseId": "%SRCROOT%" } } }] },
              { "ruleId": "LC031", "baselineState": "absent", "message": { "text": "LC031 message" },
                "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "src/My%20Orders.cs" } } }] }
            ] }] }
            """);

        var report = ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("src", "My Orders.cs"), 40, 1),
            Result("LC031", "note", InRoot("src", "My Orders.cs"), 41, 1))).ApplyBaseline(baseline);

        Assert.Equal(1, baseline.Count);
        Assert.Equal("LC031", Assert.Single(report.Findings).RuleId);
        Assert.Equal("LC007", Assert.Single(report.BaselineFindings).RuleId);
        Assert.Throws<JsonException>(() => ScanBaseline.Parse("{}"));
    }

    [Fact]
    public void Sarif_WithoutABaseline_HasFingerprintsButNoBaselineState()
    {
        using var document = JsonDocument.Parse(SarifText(ReadReport(CompilerLog(Result("LC007", "warning", InRoot("A.cs"), 1, 1)))));
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.True(result.GetProperty("partialFingerprints").TryGetProperty(ScanReport.FingerprintKey, out _));
        Assert.False(result.TryGetProperty("baselineState", out _));
    }

    [Fact]
    public void Sarif_RulesCarryDescriptionHelpAndTags()
    {
        using var document = JsonDocument.Parse(SarifText(ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("A.cs"), 1, 1),
            Result("EF1002", "warning", InRoot("A.cs"), 2, 1)))));
        var rules = document.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().ToList();

        var lc007 = rules.Single(rule => rule.GetProperty("id").GetString() == "LC007");
        Assert.Equal("Queries inside loops run once per item.", lc007.GetProperty("fullDescription").GetProperty("text").GetString());
        Assert.Equal(
            "Queries inside loops run once per item.\n\n[LC007: what it catches and how to fix it](https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html)",
            lc007.GetProperty("help").GetProperty("markdown").GetString());
        Assert.Equal(["efcore", "performance"], lc007.GetProperty("properties").GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));

        var ef1002 = rules.Single(rule => rule.GetProperty("id").GetString() == "EF1002");
        Assert.False(ef1002.TryGetProperty("fullDescription", out _));
        Assert.StartsWith("How to fix it: https://learn.microsoft.com", ef1002.GetProperty("help").GetProperty("text").GetString());
        Assert.Equal(["efcore"], ef1002.GetProperty("properties").GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public void Markdown_HasTheRuleTableAndLinkedFindings()
    {
        var report = ReadReport(CompilerLog(
            Result("LC007", "warning", InRoot("src", "My Orders.cs"), 12, 9),
            Result("LC007", "warning", InRoot("src", "Orders.cs"), 30, 9),
            Result("LC031", "note", InRoot("src", "Orders.cs"), 40, 1)));

        var markdown = GitHubOutput.Markdown(report, findingsPerRule: 1, blobUri: "https://github.com/o/r/blob/abc/");

        Assert.StartsWith("## LinqContraband: 3 EF Core query problems (2 rules, 2 files)", markdown);
        Assert.Contains("| [LC007](https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html) | Warning | 2 | N+1 Problem: Database execution inside loop |", markdown);
        Assert.Contains("<details><summary><b>LC007</b> N+1 Problem: Database execution inside loop (2)</summary>", markdown);
        Assert.Contains("- [`src/My Orders.cs:12:9`](https://github.com/o/r/blob/abc/src/My%20Orders.cs#L12): LC007 message", markdown);
        Assert.Contains("- ...and 1 more in the SARIF report.", markdown);
        Assert.Contains("- `src/Orders.cs:40:1`", GitHubOutput.Markdown(report, findingsPerRule: 5, blobUri: null));
        Assert.DoesNotContain("<details>", GitHubOutput.Markdown(report, findingsPerRule: 0, blobUri: null));
        Assert.StartsWith("## LinqContraband: no EF Core query problems", GitHubOutput.Markdown(ReadReport(CompilerLog()), 3, null));
    }

    [Fact]
    public void Annotations_PutTheMostSevereFirstAndEscapeCommandText()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Shared.cs");
        var report = ReadReport(CompilerLog(
            Result("LC031", "note", InRoot("src", "a,b.cs"), 40, 1),
            Result("LC007", "error", InRoot("src", "Orders.cs"), 12, 9),
            Result("LC007", "warning", outside, 1, 1)));

        var annotations = GitHubOutput.Annotations(report).ToList();

        Assert.Equal(
            [
                "::error file=src/Orders.cs,line=12,col=9,title=LC007 N+1 Problem%3A Database execution inside loop::LC007 message",
                "::notice file=src/a%2Cb.cs,line=40,col=1,title=LC031 Unbounded Query Materialization::LC031 message",
            ],
            annotations);
        Assert.Single(GitHubOutput.Annotations(report, max: 1));
    }

    [Fact]
    public void BlobUri_NeedsServerRepositoryAndSha()
    {
        var variables = new Dictionary<string, string>
        {
            ["GITHUB_SERVER_URL"] = "https://github.com/",
            ["GITHUB_REPOSITORY"] = "o/r",
            ["GITHUB_SHA"] = "abc",
        };
        Assert.Equal("https://github.com/o/r/blob/abc/", GitHubOutput.BlobUri(name => variables.GetValueOrDefault(name)));
        variables.Remove("GITHUB_SHA");
        Assert.Null(GitHubOutput.BlobUri(name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void Html_IsOneSelfContainedPageWithTheCodeAroundEachFinding()
    {
        var root = Directory.CreateTempSubdirectory("scan-html-").FullName;
        try
        {
            var file = Path.Combine(root, "Orders.cs");
            File.WriteAllLines(file, ["class Orders", "{", "        void A() => db.Orders.Where(o => o.Name == \"<b>\").ToList();", "}"]);
            var report = ReportIn(root,
                Result("LC007", "warning", file, 3, 21),
                Result("LC031", "note", Path.Combine(root, "Gone.cs"), 9, 1));

            var html = HtmlReport.Render(report, "9.9.9", root, new DateTimeOffset(2026, 9, 24, 21, 0, 0, TimeSpan.Zero));

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains($"<title>LinqContraband report: {Path.GetFileName(root)}</title>", html);
            Assert.Contains("scanned 2026-09-24 21:00 UTC", html);
            Assert.Contains("<span class=\"value\">2</span><span class=\"label\">problems</span>", html);
            Assert.Contains("<details class=\"rule\" id=\"LC007\" open>", html);
            Assert.Contains("Queries inside loops run once per item. <a href=\"https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html\">", html);
            Assert.Contains("<span class=\"line hit\"><span class=\"ln\">3</span>        void A() =&gt; db.Orders.Where(o =&gt; o.Name == &quot;&lt;b&gt;&quot;).ToList();</span>", html);
            Assert.Contains("<span class=\"line\"><span class=\"ln\">1</span>class Orders</span>", html);
            Assert.Contains("<input type=\"checkbox\" data-severity=\"Info\" checked>", html);
            Assert.DoesNotContain("<script src", html);
            Assert.DoesNotContain("<link", html);
            Assert.Equal(1, html.Split("<pre>").Length - 1);

            var empty = HtmlReport.Render(ReportIn(root), "9.9.9", root, DateTimeOffset.UnixEpoch);
            Assert.Contains("LinqContraband found no EF Core query problems.", empty);
            Assert.DoesNotContain("<details", empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
