using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// Leftover LC031 <c>IsRowPreservingOperator</c> names between a keyed
/// <c>Contains</c> filter and <c>GroupBy</c>. The shipped keyed-group fixtures
/// already isolate <c>Where</c> and <c>OrderBy</c>. Dropping
/// <c>OrderByDescending</c> FNs only
/// <see cref="OrderByDescending_BetweenContainsAndGroupBy_StaysQuiet"/>;
/// dropping <c>ThenBy</c> FNs only
/// <see cref="ThenBy_BetweenContainsAndGroupBy_StaysQuiet"/>;
/// dropping <c>AsNoTracking</c> FNs only
/// <see cref="AsNoTracking_BetweenContainsAndGroupBy_StaysQuiet"/>;
/// dropping <c>Include</c> FNs only
/// <see cref="Include_BetweenContainsAndGroupBy_StaysQuiet"/>
/// (and the <c>ThenInclude</c> pin that must sit after it).
/// The shipped <c>Where</c>/<c>OrderBy</c> keyed-group fixtures stay green.
/// Do not add a second <c>Where</c> or <c>OrderBy</c> pin.
/// Extra EF methods live in this file so they do not collide with the shared
/// LC031 mock or with PR #688's materializer coverage file.
/// </summary>
public sealed class UnboundedQueryMaterializationRowPreservingCoverageTests
{
    private const string Usings = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
";

    private const string EfCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsSingleQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreAutoIncludes<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => source;
        public static IQueryable<T> TagWithCallSite<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> Include<T, TProperty>(
            this IQueryable<T> source, Expression<Func<T, TProperty>> path) => source;
        public static IQueryable<T> ThenInclude<T, TPrevious, TProperty>(
            this IQueryable<T> source, Expression<Func<TPrevious, TProperty>> path) => source;
    }
}
";

    private const string Entities = @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public int TeamId { get; set; }
        public Team Team { get; set; }
    }

    public class Team
    {
        public int Id { get; set; }
        public ICollection<User> Users { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    private static string Program(string body) =>
        Usings + EfCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public object Run(AppDbContext db, List<int> ids)
        {
            " + body + @"
        }
    }
}";

    private static Task VerifyAsync(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    private const string KeyedGroupTail =
        ".GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToList();";

    [Fact]
    public Task OrderByDescending_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).OrderByDescending(u => u.Id)" + KeyedGroupTail);

    [Fact]
    public Task ThenBy_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).OrderBy(u => u.Id).ThenBy(u => u.IsActive)" + KeyedGroupTail);

    [Fact]
    public Task ThenByDescending_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).OrderBy(u => u.Id).ThenByDescending(u => u.IsActive)" + KeyedGroupTail);

    [Fact]
    public Task AsNoTracking_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).AsNoTracking()" + KeyedGroupTail);

    [Fact]
    public Task AsNoTrackingWithIdentityResolution_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).AsNoTrackingWithIdentityResolution()" + KeyedGroupTail);

    [Fact]
    public Task AsTracking_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).AsTracking()" + KeyedGroupTail);

    [Fact]
    public Task AsSplitQuery_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).AsSplitQuery()" + KeyedGroupTail);

    [Fact]
    public Task AsSingleQuery_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).AsSingleQuery()" + KeyedGroupTail);

    [Fact]
    public Task IgnoreQueryFilters_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).IgnoreQueryFilters()" + KeyedGroupTail);

    [Fact]
    public Task IgnoreAutoIncludes_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).IgnoreAutoIncludes()" + KeyedGroupTail);

    [Fact]
    public Task TagWith_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).TagWith(\"teams\")" + KeyedGroupTail);

    [Fact]
    public Task TagWithCallSite_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).TagWithCallSite()" + KeyedGroupTail);

    [Fact]
    public Task Include_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).Include(u => u.Team)" + KeyedGroupTail);

    [Fact]
    public Task ThenInclude_BetweenContainsAndGroupBy_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).Include(u => u.Team).ThenInclude<User, Team, ICollection<User>>(t => t.Users)" + KeyedGroupTail);
}
