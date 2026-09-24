using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC056_StoredProcedureComposed.StoredProcedureComposedAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC056_StoredProcedureComposed;

public partial class StoredProcedureComposedTests
{
    [Fact]
    public async Task AsTracking_PassThrough_Reports()
    {
        // Removing AsTracking from EfPassThrough stops the walk before FromSqlRaw.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsTracking().{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_PassThrough_Reports()
    {
        // Removing AsNoTrackingWithIdentityResolution from EfPassThrough hides this chain.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsNoTrackingWithIdentityResolution().{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_PassThrough_Reports()
    {
        // Removing IgnoreAutoIncludes from EfPassThrough hides this chain; IgnoreQueryFilters stays green.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").IgnoreAutoIncludes().{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task TagWithCallSite_PassThrough_Reports()
    {
        // Removing TagWithCallSite from EfPassThrough hides this chain; TagWith("blogs") stays green.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").TagWithCallSite().{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task AsSingleQuery_PassThrough_Reports()
    {
        // Removing AsSingleQuery from EfPassThrough hides this chain; AsSplitQuery stays green.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsSingleQuery().{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task ThenInclude_Composer_Reports()
    {
        // ThenInclude is composing. Dropping it from EfComposing leaves this quiet; Include stays green.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ThenInclude|}(b => b.Posts).ToList();"));
    }

    [Fact]
    public async Task ExecuteDelete_Composer_Reports()
    {
        // ExecuteDelete is composing. Dropping it from EfComposing leaves a hard DELETE over EXEC quiet.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var deleted = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ExecuteDelete|}();"));
    }

    [Fact]
    public async Task ExecuteDeleteAsync_Composer_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var deleted = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ExecuteDeleteAsync|}(ct);"));
    }

    [Fact]
    public async Task ExecuteUpdate_Composer_Reports()
    {
        // ExecuteUpdate is composing. Dropping it from EfComposing leaves a set-based write over EXEC quiet.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var updated = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ExecuteUpdate|}();"));
    }

    [Fact]
    public async Task FromSqlRaw_NamedSqlArgument_Reports()
    {
        // sql is parameter ordinal 1. Matching the argument index instead of Parameter.Ordinal
        // still works for fluent named calls; the reordered static pin below does not.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(sql: ""EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task FromSqlRaw_ReorderedStaticSqlArgument_Reports()
    {
        // source is written first in the argument list only when it is the extension receiver.
        // A static call with sql: before source: still has Parameter.Ordinal 1 for sql.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = RelationalQueryableExtensions.FromSqlRaw(sql: ""EXEC dbo.GetBlogs"", source: db.Blogs).{|LC056:Where|}(b => b.Rating > 3).ToList();"));
    }

    [Fact]
    public async Task SqlQueryRaw_NamedSqlArgument_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var ids = db.Database.SqlQueryRaw<int>(sql: ""EXEC dbo.GetIds"").{|LC056:Any|}();"));
    }

    [Fact]
    public async Task FromSqlRaw_NamedComposableSql_StaysQuiet()
    {
        // Named sql: still has to start with EXEC. A SELECT hole in the ordinal walk is not enough.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var rows = db.Blogs.FromSqlRaw(sql: ""SELECT * FROM Blogs"").Where(b => b.Rating > 3).ToList();"));
    }
}
