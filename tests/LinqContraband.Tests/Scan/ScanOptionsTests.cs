using LinqContraband.Scan;

namespace LinqContraband.Tests.Scan;

public sealed class ScanOptionsTests
{
    [Fact]
    public void NoArguments_ScansTheCurrentDirectoryWithDefaults()
    {
        Assert.True(ScanOptions.TryParse([], out var options, out var error));
        Assert.Null(error);
        Assert.Equal(".", options.Target);
        Assert.Equal("linqcontraband.sarif", options.SarifPath);
        Assert.Null(options.Configuration);
        Assert.Null(options.Framework);
        Assert.False(options.NoRestore);
        Assert.False(options.Verbose);
        Assert.Equal(10, options.Top);
        Assert.Equal(3, options.FindingsPerRule);
    }

    [Fact]
    public void EveryOption_IsParsed()
    {
        Assert.True(ScanOptions.TryParse(
            ["src/App.sln", "--sarif", "out/x.sarif", "-c", "Release", "-f", "net9.0", "--no-restore", "--top", "3", "--findings", "7", "-v"],
            out var options,
            out _));

        Assert.Equal("src/App.sln", options.Target);
        Assert.Equal("out/x.sarif", options.SarifPath);
        Assert.Equal("Release", options.Configuration);
        Assert.Equal("net9.0", options.Framework);
        Assert.True(options.NoRestore);
        Assert.Equal(3, options.Top);
        Assert.Equal(7, options.FindingsPerRule);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Filters_AccumulateAndNormalizeRuleIds()
    {
        Assert.True(ScanOptions.TryParse(
            ["--rules", "lc007, LC009", "--rules", "EF1002", "--skip-rules", "LC031", "--exclude", "tests/**", "--exclude", "Migrations", "--fail-on", "Warning", "--baseline", "base.sarif", "--summary", "s.md", "--no-github", "--html", "r.html"],
            out var options,
            out var error), error);

        Assert.Equal(["LC007", "LC009", "EF1002"], options.Rules);
        Assert.Equal(["LC031"], options.SkippedRules);
        Assert.Equal(["tests/**", "Migrations"], options.Excludes);
        Assert.Equal("Warning", options.FailOn);
        Assert.Equal("base.sarif", options.BaselinePath);
        Assert.Equal("s.md", options.SummaryPath);
        Assert.True(options.NoGitHub);
        Assert.Equal("r.html", options.HtmlPath);
    }

    [Theory]
    [InlineData("error", "Error")]
    [InlineData("info", "Info")]
    [InlineData("note", "Info")]
    [InlineData("none", null)]
    public void FailOn_TakesASeverity(string value, string? expected)
    {
        Assert.True(ScanOptions.TryParse(["--fail-on", value], out var options, out _));
        Assert.Equal(expected, options.FailOn);
    }

    [Theory]
    [InlineData("all", int.MaxValue)]
    [InlineData("ALL", int.MaxValue)]
    [InlineData("0", 0)]
    public void Findings_TakesACountOrAll(string value, int expected)
    {
        Assert.True(ScanOptions.TryParse(["--findings", value], out var options, out _));
        Assert.Equal(expected, options.FindingsPerRule);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-?")]
    public void HelpFlags_RequestHelp(string flag)
    {
        Assert.True(ScanOptions.TryParse([flag], out var options, out _));
        Assert.True(options.ShowHelp);
    }

    [Theory]
    [InlineData(new[] { "--bogus" }, "Unknown option '--bogus'.")]
    [InlineData(new[] { "--sarif" }, "--sarif needs a value.")]
    [InlineData(new[] { "-o", "--verbose" }, "-o needs a value.")]
    [InlineData(new[] { "--top", "many" }, "--top expects a whole number, got 'many'.")]
    [InlineData(new[] { "--top", "-1" }, "--top needs a value.")]
    [InlineData(new[] { "--rules", "LC007,CA1822" }, "--rules expects rule IDs such as LC007 or EF1002, got 'CA1822'.")]
    [InlineData(new[] { "--skip-rules", "," }, "--skip-rules expects rule IDs such as LC007 or EF1002, got ','.")]
    [InlineData(new[] { "--fail-on", "always" }, "--fail-on expects error, warning, info or none, got 'always'.")]
    [InlineData(new[] { "--findings", "some" }, "--findings expects a whole number or 'all', got 'some'.")]
    [InlineData(new[] { "a.sln", "b.sln" }, "Only one path can be scanned at a time; got 'a.sln' and 'b.sln'.")]
    public void BadArguments_ReportWhatIsWrong(string[] args, string expected)
    {
        Assert.False(ScanOptions.TryParse(args, out _, out var error));
        Assert.Equal(expected, error);
    }
}
