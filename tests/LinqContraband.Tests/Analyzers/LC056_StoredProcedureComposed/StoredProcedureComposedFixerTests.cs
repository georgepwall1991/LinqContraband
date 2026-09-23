using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC056_StoredProcedureComposed.StoredProcedureComposedAnalyzer,
    LinqContraband.Analyzers.LC056_StoredProcedureComposed.StoredProcedureComposedFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC056_StoredProcedureComposed;

public class StoredProcedureComposedFixerTests
{
    private static string Wrap(string body) => StoredProcedureComposedTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Fact]
    public async Task InsertsAsEnumerableBeforeComposition()
    {
        await VerifyFixAsync(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3).ToList();",
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsEnumerable().Where(b => b.Rating > 3).ToList();");
    }

    [Fact]
    public async Task KeepsPassThroughOperatorsOnTheQuery()
    {
        await VerifyFixAsync(@"
        var rows = db.Blogs
            .FromSql($""EXEC dbo.GetBlogs {tenantId}"")
            .AsNoTracking()
            .{|LC056:OrderBy|}(b => b.Name)
            .ToList();", @"
        var rows = db.Blogs
            .FromSql($""EXEC dbo.GetBlogs {tenantId}"")
            .AsNoTracking().AsEnumerable()
            .OrderBy(b => b.Name)
            .ToList();");
    }

    [Fact]
    public async Task SyncTerminal_UsesAsEnumerable()
    {
        await VerifyFixAsync(
            @"var blog = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlog"").{|LC056:FirstOrDefault|}();",
            @"var blog = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlog"").AsEnumerable().FirstOrDefault();");
    }

    [Theory]
    // Include needs the queryable.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:Include|}(b => b.Posts).ToList();")]
    // The result must stay an IQueryable.
    [InlineData(@"IQueryable<Blog> rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3);")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3); var list = await rows.ToListAsync(ct);")]
    public async Task RewriteWouldNotCompile_NoFix(string code)
    {
        await VerifyNoFixAsync(code);
    }

    [Fact]
    public async Task FixAll_FixesEveryComposedCall()
    {
        var before = Wrap(@"
        var first = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3).ToList();
        var second = db.Database.SqlQueryRaw<int>(""EXEC dbo.GetIds"").{|LC056:Any|}();");
        var after = Wrap(@"
        var first = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsEnumerable().Where(b => b.Rating > 3).ToList();
        var second = db.Database.SqlQueryRaw<int>(""EXEC dbo.GetIds"").AsEnumerable().Any();");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }
}
