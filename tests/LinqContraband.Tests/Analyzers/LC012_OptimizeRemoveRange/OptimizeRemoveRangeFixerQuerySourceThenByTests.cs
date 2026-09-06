using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    // Local mock so this file does not edit the shared LC012 EFCoreMock used by
    // other open coverage PRs. ThenBy / ThenByDescending are mocked on IQueryable
    // so each name can be mutated without a predecessor OrderBy (which is already
    // on the allow-list and would hide a ThenBy regression).
    // Keep the fake operators in Microsoft.EntityFrameworkCore — not System.Linq —
    // so the namespace gate still applies.
    private const string EFCoreMockWithThenBy = @"
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

        public static IQueryable<TSource> ThenBy<TSource, TKey>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, TKey>> keySelector) => source;

        public static IQueryable<TSource> ThenByDescending<TSource, TKey>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, TKey>> keySelector) => source;
    }
}
";

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesThenBy()
    {
        await VerifyDifferentFreshContextThenByFixAsync("deleteDb.Users.ThenBy(x => x.Id)");
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenDifferentFreshContextQueryUsesThenByDescending()
    {
        await VerifyDifferentFreshContextThenByFixAsync("deleteDb.Users.ThenByDescending(x => x.Id)");
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenDifferentFreshContextQueryUsesLookalikeThenBy()
    {
        var extras = @"
namespace QueryLookalikes
{
    public static class QueryExtensions
    {
        public static System.Linq.IQueryable<TSource> ThenBy<TSource, TKey>(
            this System.Linq.IQueryable<TSource> source,
            System.Linq.Expressions.Expression<System.Func<TSource, TKey>> keySelector) => source;
    }
}
";
        var test = DifferentFreshContextThenBySource(
            "QueryLookalikes.QueryExtensions.ThenBy(deleteDb.Users, x => x.Id)",
            markDiagnostic: true,
            extras);

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    private static async Task VerifyDifferentFreshContextThenByFixAsync(string queryExpression)
    {
        var test = DifferentFreshContextThenBySource(queryExpression, markDiagnostic: true);
        var fixedCode = DifferentFreshContextThenBySource(queryExpression, markDiagnostic: false)
            .Replace(
                "            deleteDb.Users.RemoveRange(query);",
                "            // Warning: ExecuteDelete bypasses change tracking and cascades.\n            query.ExecuteDelete();");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    private static string DifferentFreshContextThenBySource(string queryExpression, bool markDiagnostic, string extras = "")
    {
        var removeRange = markDiagnostic
            ? "{|LC012:deleteDb.Users.RemoveRange(query)|};"
            : "deleteDb.Users.RemoveRange(query);";

        return @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMockWithThenBy + extras + @"
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
