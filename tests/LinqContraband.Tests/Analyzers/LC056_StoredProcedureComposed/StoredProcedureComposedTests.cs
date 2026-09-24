using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC056_StoredProcedureComposed.StoredProcedureComposedAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC056_StoredProcedureComposed;

public partial class StoredProcedureComposedTests
{
    private const string Usings = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
";

    private const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
    }

    public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this DbSet<TEntity> source, string sql, params object[] parameters) where TEntity : class => source;
        public static IQueryable<TEntity> FromSql<TEntity>(this DbSet<TEntity> source, FormattableString sql) where TEntity : class => source;
        public static IQueryable<TEntity> FromSqlInterpolated<TEntity>(this DbSet<TEntity> source, FormattableString sql) where TEntity : class => source;
        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> AsSingleQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static int ExecuteDelete<TEntity>(this IQueryable<TEntity> source) => 0;
        public static Task<int> ExecuteDeleteAsync<TEntity>(this IQueryable<TEntity> source, CancellationToken cancellationToken = default) => null;
        public static int ExecuteUpdate<TEntity>(this IQueryable<TEntity> source) => 0;
        public static Task<int> ExecuteUpdateAsync<TEntity>(this IQueryable<TEntity> source, CancellationToken cancellationToken = default) => null;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static IQueryable<TResult> SqlQueryRaw<TResult>(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => null;
        public static IQueryable<TResult> SqlQuery<TResult>(this Infrastructure.DatabaseFacade databaseFacade, FormattableString sql) => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => source;
        public static IQueryable<T> TagWithCallSite<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> IgnoreAutoIncludes<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> Include<T, TProperty>(this IQueryable<T> source, Expression<Func<T, TProperty>> path) where T : class => source;
        public static IQueryable<T> ThenInclude<T, TProperty>(this IQueryable<T> source, Expression<Func<T, TProperty>> path) where T : class => source;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade { }
}

public class Blog
{
    public int Id { get; set; }
    public int Rating { get; set; }
    public string Name { get; set; }
    public List<Post> Posts { get; set; }
}

public class Post { public int Id { get; set; } }

public class BlogContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
}

public static class QueryHelpers
{
    public static IQueryable<T> Page<T>(this IQueryable<T> source, int page) => source;
}";

    internal static string Wrap(string body) => Usings + @"
class Program
{
    async Task Run(BlogContext db, int tenantId, string procedure, CancellationToken ct)
    {
" + body + @"
    }
}
" + EfMock;

    [Theory]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|#0:Where|}(b => b.Rating > 3).ToList();", "Where", "FromSqlRaw")]
    [InlineData(@"var rows = db.Blogs.FromSql($""EXEC dbo.GetBlogs {tenantId}"").{|#0:OrderBy|}(b => b.Name).ToList();", "OrderBy", "FromSql")]
    [InlineData(@"var rows = db.Blogs.FromSqlInterpolated($""execute dbo.GetBlogs {tenantId}"").{|#0:Select|}(b => b.Name).ToList();", "Select", "FromSqlInterpolated")]
    [InlineData(@"var blog = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlog @id"", tenantId).{|#0:FirstOrDefault|}();", "FirstOrDefault", "FromSqlRaw")]
    [InlineData(@"var blog = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlog"").{|#0:FirstOrDefaultAsync|}(ct);", "FirstOrDefaultAsync", "FromSqlRaw")]
    [InlineData(@"var count = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|#0:CountAsync|}(ct);", "CountAsync", "FromSqlRaw")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").{|#0:Include|}(b => b.Posts).ToList();", "Include", "FromSqlRaw")]
    [InlineData(@"var ids = db.Database.SqlQuery<int>($""EXEC dbo.GetIds {tenantId}"").{|#0:Where|}(id => id > 0).ToList();", "Where", "SqlQuery")]
    [InlineData(@"var ids = db.Database.SqlQueryRaw<int>(""EXEC dbo.GetIds"").{|#0:Any|}();", "Any", "SqlQueryRaw")]
    public async Task ComposedStoredProcedure_Reports(string body, string composer, string source)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0).WithArguments(composer, source));
    }

    [Theory]
    // Pass-through operators before the composition.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsNoTracking().TagWith(""blogs"").{|LC056:Where|}(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").IgnoreQueryFilters().AsSplitQuery().AsQueryable().{|LC056:Take|}(10).ToList();")]
    // Leading whitespace and SQL comments are skipped, as EF Core does.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""  -- blogs\n  /* cached */ EXEC dbo.GetBlogs"").{|LC056:Where|}(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(@""
            EXEC dbo.GetBlogs"").{|LC056:Skip|}(10).ToList();")]
    // Static call.
    [InlineData(@"var rows = Queryable.{|LC056:Where|}(db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs""), b => b.Rating > 3).ToList();")]
    public async Task ComposedStoredProcedureShapes_Report(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Theory]
    // Not composed: runs the SQL as it is.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").ToList();")]
    [InlineData(@"var rows = await db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsNoTracking().ToListAsync(ct);")]
    [InlineData(@"foreach (var blog in db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"")) { }")]
    [InlineData(@"var stream = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsAsyncEnumerable();")]
    // Composition after switching to memory.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").AsEnumerable().Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").ToList().Where(b => b.Rating > 3).ToList();")]
    // An identity Select changes nothing in the SQL.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").Select(b => b).ToList();")]
    // Composable SQL.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""SELECT * FROM Blogs"").Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSql($""SELECT * FROM dbo.GetBlogs({tenantId})"").Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXECUTIONS_VIEW"").Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""-- EXEC dbo.GetBlogs\nSELECT * FROM Blogs"").Where(b => b.Rating > 3).ToList();")]
    // SQL text not known at compile time.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(procedure).Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSql($""{procedure}"").Where(b => b.Rating > 3).ToList();")]
    // Stored in a local or passed to a helper: not followed.
    [InlineData(@"var query = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs""); var rows = query.Where(b => b.Rating > 3).ToList();")]
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").Page(2).ToList();")]
    // Cast can be a no-op.
    [InlineData(@"var rows = db.Blogs.FromSqlRaw(""EXEC dbo.GetBlogs"").Cast<Blog>().ToList();")]
    public async Task NotComposedOrComposable_NoDiagnostic(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }
}
