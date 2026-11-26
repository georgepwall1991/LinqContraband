using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC056_StoredProcedureComposed.StoredProcedureComposedAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC056_StoredProcedureComposed;

/// <summary>
/// Leftover LC056 <c>EfComposing</c> names the shipped
/// <c>Include</c>/<c>CountAsync</c>/<c>FirstOrDefaultAsync</c> fixtures and the
/// leftover <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> pins do not isolate.
/// Dropping one leftover name FNs only that pin. Queryable composers
/// (<c>Where</c>, <c>Any</c>, <c>Take</c>) stay green through the
/// <c>System.Linq.Queryable</c> arm. A leftover lookalike name on an unrelated
/// type stays quiet through the containing-type gate. Does not restage leftover
/// pass-through or leftover ExecuteDelete/ExecuteUpdate files.
/// </summary>
public partial class StoredProcedureComposedTests
{
    private static string LeftoverComposerWrap(string body)
    {
        return Wrap(body).Replace(
            "        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;",
            @"        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static IQueryable<T> ThenInclude<T, TProperty>(this IQueryable<T> source, Expression<Func<T, TProperty>> path) where T : class => source;
        public static Task<bool> AllAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<double> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<bool> ContainsAsync<T>(this IQueryable<T> source, T item, CancellationToken cancellationToken = default) => null;
        public static Task<long> LongCountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> ElementAtAsync<T>(this IQueryable<T> source, int index, CancellationToken cancellationToken = default) => null;
        public static Task<T> ElementAtOrDefaultAsync<T>(this IQueryable<T> source, int index, CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> LastAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> LastOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> MaxAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<int> MinAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<T> SingleAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> SingleOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) => null;");
    }

    [Fact]
    public async Task LeftoverThenInclude_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ThenInclude|}(b => b.Posts).ToList();"));
    }

    [Fact]
    public async Task LeftoverAnyAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var any = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:AnyAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverAllAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var all = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:AllAsync|}(b => b.Rating > 0, ct);"));
    }

    [Fact]
    public async Task LeftoverAverageAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var average = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:AverageAsync|}(b => b.Rating, ct);"));
    }

    [Fact]
    public async Task LeftoverContainsAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var found = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ContainsAsync|}(new Blog(), ct);"));
    }

    [Fact]
    public async Task LeftoverLongCountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var count = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:LongCountAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverElementAtAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ElementAtAsync|}(0, ct);"));
    }

    [Fact]
    public async Task LeftoverElementAtOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:ElementAtOrDefaultAsync|}(0, ct);"));
    }

    [Fact]
    public async Task LeftoverFirstAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:FirstAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverLastAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:LastAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverLastOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:LastOrDefaultAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverMaxAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var max = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:MaxAsync|}(b => b.Rating, ct);"));
    }

    [Fact]
    public async Task LeftoverMinAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var min = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:MinAsync|}(b => b.Rating, ct);"));
    }

    [Fact]
    public async Task LeftoverSingleAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:SingleAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverSingleOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:SingleOrDefaultAsync|}(ct);"));
    }

    [Fact]
    public async Task LeftoverSumAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverComposerWrap(
            @"var sum = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|LC056:SumAsync|}(b => b.Rating, ct);"));
    }

    [Fact]
    public async Task LookalikeAnyAsync_OnUnrelatedQueryable_StaysQuiet()
    {
        // The leftover EF AnyAsync mock is omitted so this binds only to the
        // lookalike. Classify stops unless the containing type is EF Core's.
        var lookalike = @"
public static class LeftoverComposerLookalikes
{
    public static Task<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
}
";
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"var any = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AnyAsync(ct);")
            + lookalike);
    }
}
