using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

/// <summary>
/// EF Core 8+ reports direct interpolation as EF1002 and EF Core 10+ reports direct concatenation as
/// EF1003, on exactly the calls below. LC018 stays quiet there instead of adding a second warning.
/// </summary>
public class AvoidFromSqlRawWithInterpolationEfAnalyzerOverlapTests
{
    private const string Usage = @"
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
}

public class Repository
{
    public void Run(AppDbContext db, int id, string name)
    {
        BODY
    }
}
";

    // Bodies mark an expected report with {|LC018:...|}; its message always names FromSql because the
    // relational mock declares it.
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
    [InlineData("11.0.0.0")]
    public Task Interpolation_OnEf8OrLater_IsLeftToEf1002(string efVersion) =>
        VerifyAsync(efVersion, @"var users = db.Users.FromSqlRaw($""SELECT * FROM Users WHERE Id = {id}"");");

    [Fact]
    public Task Interpolation_WithExtraParameters_OnEf8_IsLeftToEf1002() =>
        VerifyAsync("8.0.0.0", @"var users = db.Users.FromSqlRaw($""SELECT * FROM Users WHERE Id = {id}"", 1);");

    [Fact]
    public Task Interpolation_StaticExtensionForm_OnEf8_IsLeftToEf1002() =>
        VerifyAsync("8.0.0.0", @"var users = RelationalQueryableExtensions.FromSqlRaw(db.Users, $""SELECT * FROM Users WHERE Id = {id}"");");

    [Fact]
    public Task SqlQueryRawInterpolation_OnEf8_IsLeftToEf1002() =>
        VerifyAsync("8.0.0.0", @"var ids = db.Database.SqlQueryRaw<int>($""SELECT Id FROM Users WHERE Name = {name}"");");

    [Fact]
    public Task Interpolation_OnEf7_StillReports() =>
        VerifyAsync(
            "7.0.0.0",
            @"var users = db.Users.FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});");

    [Fact]
    public Task Concatenation_OnEf10_IsLeftToEf1003() =>
        VerifyAsync("10.0.0.0", @"var users = db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = "" + name);");

    [Fact]
    public Task Concatenation_OnEf9_StillReports_BecauseEf1003ArrivedInEf10() =>
        VerifyAsync(
            "9.0.0.0",
            @"var users = db.Users.FromSqlRaw({|LC018:""SELECT * FROM Users WHERE Name = "" + name|});");

    [Fact]
    public Task ReorderedNamedSqlArgument_StillReports_BecauseEf1002ReadsThePositionalSlot() =>
        VerifyAsync(
            "10.0.0.0",
            @"var users = db.Users.FromSqlRaw(parameters: new object[0], sql: {|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});");

    [Fact]
    public Task DeferralTurnedOff_StillReports() =>
        VerifyAsync(
            "10.0.0.0",
            @"var users = db.Users.FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});",
            "dotnet_code_quality.LC018.defer_to_ef_analyzers = false\n");
}
