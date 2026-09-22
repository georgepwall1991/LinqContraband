using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    // Local mock so this file does not edit the shared LC012 EFCoreMock used by
    // other open coverage PRs. Include is the remaining allow-list name that
    // needs a navigation property; ThenInclude is mocked on IQueryable so its
    // name can be mutated without a predecessor Include.
    private const string EFCoreMockWithInclude = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
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

        public static IQueryable<TEntity> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            where TEntity : class => source;

        public static IQueryable<TEntity> ThenInclude<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            where TEntity : class => source;
    }
}
";

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesInclude()
    {
        await VerifyDifferentFreshContextIncludeFixAsync("deleteDb.Users.Include(x => x.Orders)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesThenInclude()
    {
        await VerifyDifferentFreshContextIncludeFixAsync("deleteDb.Users.ThenInclude(x => x.Orders)");
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenDifferentFreshContextQueryUsesLookalikeInclude()
    {
        var extras = @"
namespace QueryLookalikes
{
    public static class QueryExtensions
    {
        public static System.Linq.IQueryable<TEntity> Include<TEntity, TProperty>(
            this System.Linq.IQueryable<TEntity> source,
            System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> navigationPropertyPath)
            where TEntity : class => source;
    }
}
";
        var test = DifferentFreshContextIncludeSource(
            "QueryLookalikes.QueryExtensions.Include(deleteDb.Users, x => x.Orders)",
            markDiagnostic: true,
            extras);

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    private static async Task VerifyDifferentFreshContextIncludeFixAsync(string queryExpression)
    {
        var test = DifferentFreshContextIncludeSource(queryExpression, markDiagnostic: true);
        var fixedCode = DifferentFreshContextIncludeSource(queryExpression, markDiagnostic: false)
            .Replace(
                "            deleteDb.Users.RemoveRange(query);",
                "            // Warning: ExecuteDelete bypasses change tracking and cascades.\n            query.ExecuteDelete();");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    private static string DifferentFreshContextIncludeSource(string queryExpression, bool markDiagnostic, string extras = "")
    {
        var removeRange = markDiagnostic
            ? "{|LC012:deleteDb.Users.RemoveRange(query)|};"
            : "deleteDb.Users.RemoveRange(query);";

        return @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMockWithInclude + extras + @"
namespace LinqContraband.Test
{
    public class Order { public int Id { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public System.Collections.Generic.ICollection<Order> Orders { get; set; }
    }
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
