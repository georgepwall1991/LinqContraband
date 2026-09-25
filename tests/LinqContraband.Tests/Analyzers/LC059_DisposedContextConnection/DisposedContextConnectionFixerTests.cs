using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC059_DisposedContextConnection.DisposedContextConnectionAnalyzer,
    LinqContraband.Analyzers.LC059_DisposedContextConnection.DisposedContextConnectionFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC059_DisposedContextConnection;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class DisposedContextConnectionFixerTests
{
    private static string Wrap(string body) => DisposedContextConnectionTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Theory]
    [InlineData(
        @"using var connection = {|LC059:db.Database.GetDbConnection()|}; await connection.OpenAsync(ct);",
        @"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct);")]
    [InlineData(
        @"await using var connection = {|LC059:db.Database.GetDbConnection()|}; await connection.OpenAsync(ct);",
        @"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct);")]
    [InlineData(
        @"using IDbConnection connection = {|LC059:db.Database.GetDbConnection()|}; connection.Open();",
        @"IDbConnection connection = db.Database.GetDbConnection(); connection.Open();")]
    [InlineData(
        @"using (var connection = {|LC059:db.Database.GetDbConnection()|}) { await connection.OpenAsync(ct); }",
        @"{ var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); }")]
    [InlineData(
        @"await using (var connection = {|LC059:(DbConnection)db.Database.GetDbConnection()|}) { await connection.OpenAsync(ct); }",
        @"{ var connection = (DbConnection)db.Database.GetDbConnection(); await connection.OpenAsync(ct); }")]
    [InlineData(
        @"var connection = db.Database.GetDbConnection(); using ({|LC059:connection|}) { connection.Open(); }",
        @"var connection = db.Database.GetDbConnection(); { connection.Open(); }")]
    [InlineData(
        @"var connection = db.Database.GetDbConnection(); try { connection.Open(); } finally { {|LC059:connection.Dispose()|}; }",
        @"var connection = db.Database.GetDbConnection(); try { connection.Open(); } finally { }")]
    public async Task RemovesTheDisposal(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }

    [Fact]
    public async Task UsingStatementBlock_BecomesAPlainBlock()
    {
        await VerifyFixAsync(@"
        using (var connection = {|LC059:db.Database.GetDbConnection()|})
        {
            // Open it for the raw command.
            await connection.OpenAsync(ct);
        }", @"
        {
            var connection = db.Database.GetDbConnection();
            // Open it for the raw command.
            await connection.OpenAsync(ct);
        }");
    }

    [Fact]
    public async Task StackedUsingStatements_KeepTheInnerUsing()
    {
        await VerifyFixAsync(@"
        using (var connection = {|LC059:db.Database.GetDbConnection()|})
        using (var command = connection.CreateCommand())
        {
            command.CommandText = ""SELECT 1"";
        }", @"
        {
            var connection = db.Database.GetDbConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = ""SELECT 1"";
            }
        }");
    }

    [Fact]
    public async Task ExplicitDispose_RemovesTheStatement()
    {
        await VerifyFixAsync(@"
        var connection = db.Database.GetDbConnection();
        connection.Open();
        {|LC059:connection.Dispose()|};
        connection.Close();", @"
        var connection = db.Database.GetDbConnection();
        connection.Open();
        connection.Close();");
    }

    [Fact]
    public async Task ExplicitDisposeAsync_RemovesTheStatement()
    {
        await VerifyFixAsync(@"
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await {|LC059:connection.DisposeAsync()|}.ConfigureAwait(false);
        await connection.CloseAsync();", @"
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await connection.CloseAsync();");
    }

    [Theory]
    // Removing the statement would leave the if without a body.
    [InlineData(@"var connection = db.Database.GetDbConnection(); if (ct.CanBeCanceled) {|LC059:connection.Dispose()|};")]
    // The other declarator is disposed by the same using.
    [InlineData(@"using DbConnection connection = {|LC059:db.Database.GetDbConnection()|}, owned = ConnectionFactory.Create();")]
    // The disposal is part of a larger expression.
    [InlineData(@"var connection = db.Database.GetDbConnection(); Action close = () => {|LC059:connection.Dispose()|};")]
    public async Task NoSafeRewrite_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code);
    }

    [Fact]
    public async Task FixAll_FixesEveryDisposal()
    {
        var before = Wrap(@"
        using var first = {|LC059:db.Database.GetDbConnection()|};
        var second = db.Database.GetDbConnection();
        {|LC059:second.Dispose()|};");
        var after = Wrap(@"
        var first = db.Database.GetDbConnection();
        var second = db.Database.GetDbConnection();");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    /// <summary>The fixer-coverage corpus: every shape the analyzer tests report gets a compiling fix.</summary>
    [Theory]
    [InlineData(
        @"using ({|LC059:db.Database.GetDbConnection()|}) { }",
        @"{ }")]
    [InlineData(
        @"var timeout = 30; {|LC059:db.Database.GetDbConnection().Dispose()|};",
        @"var timeout = 30;")]
    [InlineData(
        @"var connection = db.Database.GetDbConnection(); connection.Open(); {|LC059:connection.Dispose()|};",
        @"var connection = db.Database.GetDbConnection(); connection.Open();")]
    [InlineData(
        @"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); await {|LC059:connection.DisposeAsync()|};",
        @"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct);")]
    public async Task ReportedShapes_FixCompiles(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }
}
