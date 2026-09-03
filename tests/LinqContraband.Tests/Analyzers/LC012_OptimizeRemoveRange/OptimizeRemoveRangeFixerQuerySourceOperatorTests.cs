using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    private const string EFCoreMockWithTransparentOperators = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void RemoveRange(IEnumerable<TEntity> entities) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsNoTrackingWithIdentityResolution<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsSplitQuery<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsSingleQuery<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
        public static IQueryable<TSource> IgnoreQueryFilters<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesOrderBy()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.OrderBy(x => x.Id)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesOrderByDescending()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.OrderByDescending(x => x.Id)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesSkip()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.Skip(10)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesTake()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.Take(10)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesDistinct()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.Distinct()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesReverse()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.Reverse()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsQueryable()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsQueryable()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsTracking()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsTracking()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsNoTracking()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsNoTracking()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsNoTrackingWithIdentityResolution()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsNoTrackingWithIdentityResolution()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsSplitQuery()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsSplitQuery()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesAsSingleQuery()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.AsSingleQuery()");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesTagWith()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.TagWith(\"purge\")");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesIgnoreQueryFilters()
    {
        await VerifyDifferentFreshContextFixAsync("deleteDb.Users.IgnoreQueryFilters()");
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenDifferentFreshContextQueryUsesLookalikeTagWith()
    {
        var extras = @"
namespace QueryLookalikes
{
    public static class QueryExtensions
    {
        public static System.Linq.IQueryable<TSource> TagWith<TSource>(this System.Linq.IQueryable<TSource> source, string tag) => source;
    }
}
";
        var test = DifferentFreshContextSource(
            "QueryLookalikes.QueryExtensions.TagWith(deleteDb.Users, \"purge\")",
            markDiagnostic: true,
            extras);

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    private static async Task VerifyDifferentFreshContextFixAsync(string queryExpression)
    {
        var test = DifferentFreshContextSource(queryExpression, markDiagnostic: true);
        var fixedCode = DifferentFreshContextSource(queryExpression, markDiagnostic: false)
            .Replace(
                "            deleteDb.Users.RemoveRange(query);",
                "            // Warning: ExecuteDelete bypasses change tracking and cascades.\n            query.ExecuteDelete();");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    private static string DifferentFreshContextSource(string queryExpression, bool markDiagnostic, string extras = "")
    {
        var removeRange = markDiagnostic
            ? "{|LC012:deleteDb.Users.RemoveRange(query)|};"
            : "deleteDb.Users.RemoveRange(query);";

        return @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMockWithTransparentOperators + extras + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public class TestClass
    {
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = " + queryExpression + @";

            " + removeRange + @"
            saveDb.SaveChanges();
        }
    }
}";
    }
}
