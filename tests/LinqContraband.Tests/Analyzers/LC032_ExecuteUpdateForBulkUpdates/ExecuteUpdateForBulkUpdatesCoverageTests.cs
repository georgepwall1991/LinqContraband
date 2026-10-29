using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Leftover <c>AllowedQuerySteps</c> and <c>MaterializerSteps</c> names the
/// shipped fixtures do not isolate. <c>Where</c>, <c>OrderBy</c>, <c>Take</c>,
/// <c>ToList</c> and <c>ToListAsync</c> already have crime cases. Dropping a
/// leftover name fails only that operator's pin. <c>Select</c> is not an
/// allowed step and stays quiet so a later edit that treats unknown operators
/// as EF-scoped is visible. <c>ThenInclude</c> is reached through
/// <c>Include</c>; dropping <c>Include</c> also fails the ThenInclude pin.
/// Lookalike helpers that share an allowed name still report (name-only gate)
/// and are not locked as quiet.
/// </summary>
public partial class ExecuteUpdateForBulkUpdatesTests
{
    private const string CoverageUsings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestApp;
";

    private const string CoverageEfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
        public static Task<TSource[]> ToArrayAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(Array.Empty<TSource>());
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, object updates) => 0;
        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source, object updates) => Task.FromResult(0);
        public static IQueryable<TSource> Include<TSource, TProperty>(this IQueryable<TSource> source, Expression<Func<TSource, TProperty>> navigation) => source;
        public static IQueryable<TSource> ThenInclude<TSource, TProperty>(this IQueryable<TSource> source, Expression<Func<TSource, TProperty>> navigation) => source;
        public static IQueryable<TSource> IgnoreQueryFilters<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsSplitQuery<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsSingleQuery<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> IgnoreAutoIncludes<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
        public static IQueryable<TSource> TagWithCallSite<TSource>(this IQueryable<TSource> source, string filePath = """", int lineNumber = 0) => source;
    }
}
";

    private const string CoverageTypes = @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
        public List<Order> Orders { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    private static string Crime(string query, bool async = false)
    {
        if (async)
        {
            return CoverageUsings + CoverageEfMock + CoverageTypes + $@"
class Program
{{
    async Task Run()
    {{
        using var db = new AppDbContext();
        {{|LC032:foreach (var user in {query})
        {{
            user.Name = ""Archived"";
        }}|}}
        await db.SaveChangesAsync();
    }}
}}";
        }

        return CoverageUsings + CoverageEfMock + CoverageTypes + $@"
class Program
{{
    void Run()
    {{
        using var db = new AppDbContext();
        {{|LC032:foreach (var user in {query})
        {{
            user.Name = ""Archived"";
        }}|}}
        db.SaveChanges();
    }}
}}";
    }

    private static string Quiet(string query) =>
        CoverageUsings + CoverageEfMock + CoverageTypes + $@"
class Program
{{
    void Run()
    {{
        using var db = new AppDbContext();
        foreach (var user in {query})
        {{
            user.Name = ""Archived"";
        }}
        db.SaveChanges();
    }}
}}";

    [Fact]
    public async Task OrderByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).OrderByDescending(u => u.Id)"));
    }

    [Fact]
    public async Task ThenBy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).ThenBy(u => u.Name)"));
    }

    [Fact]
    public async Task ThenByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).ThenByDescending(u => u.Name)"));
    }

    [Fact]
    public async Task Skip_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).Skip(1)"));
    }

    [Fact]
    public async Task Distinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).Distinct()"));
    }

    [Fact]
    public async Task Include_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Include(u => u.Orders)"));
    }

    [Fact]
    public async Task ThenInclude_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Include(u => u.Orders).ThenInclude(o => o.Id)"));
    }

    [Fact]
    public async Task IgnoreQueryFilters_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.IgnoreQueryFilters()"));
    }

    [Fact]
    public async Task AsSplitQuery_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.AsSplitQuery()"));
    }

    [Fact]
    public async Task AsSingleQuery_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.AsSingleQuery()"));
    }

    [Fact]
    public async Task AsTracking_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.AsTracking()"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.IgnoreAutoIncludes()"));
    }

    [Fact]
    public async Task TagWith_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.TagWith(\"archive\")"));
    }

    [Fact]
    public async Task TagWithCallSite_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.TagWithCallSite()"));
    }

    [Fact]
    public async Task ToArray_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("db.Users.Where(u => u.IsActive).ToArray()"));
    }

    [Fact]
    public async Task ToArrayAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Crime("await db.Users.Where(u => u.IsActive).ToArrayAsync()", async: true));
    }

    [Fact]
    public async Task Select_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Quiet("db.Users.Select(u => u)"));
    }
}
