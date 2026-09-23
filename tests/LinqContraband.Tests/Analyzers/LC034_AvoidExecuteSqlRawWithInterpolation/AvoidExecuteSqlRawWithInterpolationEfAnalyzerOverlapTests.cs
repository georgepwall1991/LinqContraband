using LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

/// <summary>
/// EF Core 8+ reports direct interpolation as EF1002 and EF Core 10+ reports direct concatenation as
/// EF1003 on ExecuteSqlRaw/ExecuteSqlRawAsync. LC034 stays quiet there instead of adding a second warning.
/// </summary>
public class AvoidExecuteSqlRawWithInterpolationEfAnalyzerOverlapTests
{
    private const string Usage = @"
using Microsoft.EntityFrameworkCore;

public class Repository
{
    public void Run(DbContext db, int id)
    {
        BODY
    }
}
";

    private static Task VerifyAsync(string efVersion, string body, string? editorConfig = null)
    {
        var test = new AnalyzerTest { TestCode = Usage.Replace("BODY", body) };
        EfRelationalAssemblyMock.AddTo(test.TestState, efVersion);
        if (editorConfig != null)
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n[*]\n" + editorConfig));
        return test.RunAsync();
    }

    [Theory]
    [InlineData("8.0.0.0")]
    [InlineData("10.0.0.0")]
    public Task Interpolation_OnEf8OrLater_IsLeftToEf1002(string efVersion) =>
        VerifyAsync(efVersion, @"db.Database.ExecuteSqlRaw($""DELETE FROM Users WHERE Id = {id}"");");

    [Fact]
    public Task AsyncInterpolation_OnEf8_IsLeftToEf1002() =>
        VerifyAsync("8.0.0.0", @"db.Database.ExecuteSqlRawAsync($""DELETE FROM Users WHERE Id = {id}"");");

    [Fact]
    public Task Concatenation_OnEf10_IsLeftToEf1003() =>
        VerifyAsync("10.0.0.0", @"db.Database.ExecuteSqlRaw(""DELETE FROM Users WHERE Id = "" + id);");

    [Fact]
    public Task Concatenation_OnEf8_StillReports() =>
        VerifyAsync("8.0.0.0", @"db.Database.ExecuteSqlRaw({|LC034:""DELETE FROM Users WHERE Id = "" + id|});");

    [Fact]
    public Task Interpolation_OnEf7_StillReports() =>
        VerifyAsync("7.0.0.0", @"db.Database.ExecuteSqlRaw({|LC034:$""DELETE FROM Users WHERE Id = {id}""|});");

    [Fact]
    public Task DeferralTurnedOff_StillReports() =>
        VerifyAsync(
            "10.0.0.0",
            @"db.Database.ExecuteSqlRaw({|LC034:$""DELETE FROM Users WHERE Id = {id}""|});",
            "dotnet_code_quality.LC034.defer_to_ef_analyzers = false\n");
}
