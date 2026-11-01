using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// Leftover LC031 <c>IsCollectionMaterializer</c>, <c>IsBoundingMethod</c> and
/// <c>IsAggregateMethod</c> names the shipped ToList/ToArray/ToLookup,
/// Take, Count/Max, and FirstOrDefault-as-terminal fixtures do not isolate.
/// Dropping <c>ToHashSet</c> FNs only <see cref="ToHashSet_FromDbSet_Reports"/>;
/// dropping <c>ToArrayAsync</c> FNs only <see cref="ToArrayAsync_FromDbSet_Reports"/>.
/// The shipped <c>ToList</c> and <c>ToLookup</c> fixtures stay green.
/// Dropping <c>TakeWhile</c> FNs only <see cref="TakeWhile_ThenToList_StaysQuiet"/>.
/// Dropping <c>All</c> FNs only <see cref="All_KeyedGroup_StaysQuiet"/>.
/// Dropping <c>ExecuteUpdate</c> from the keyed-group exclusion FNs only
/// <see cref="ExecuteUpdate_KeyedGroup_StillReports"/>.
/// <c>Find</c>/<c>FindAsync</c> cannot isolate: <c>DbSet.Find</c> returns the
/// entity, so it never appears as the previous invocation of a collection
/// materializer. <c>First</c>/<c>Single</c>/<c>Last</c> after
/// <c>Select(u =&gt; u.Orders)</c> stay quiet because
/// <c>IsQueryExecutingMaterializer</c> stops the walk, not because they are
/// bounding names. Do not lock those as quiet or as bounding pins.
/// Do not add a second <c>ToList</c>, <c>ToArray</c>, <c>ToLookup</c> or
/// <c>Take</c> pin.
/// </summary>
public sealed class UnboundedQueryMaterializationCoverageTests
{
    private const string Usings = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
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
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(Array.Empty<T>());
        public static Task<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(
            this IQueryable<T> source, Func<T, TKey> keySelector) =>
            Task.FromResult(new Dictionary<TKey, T>());
        public static HashSet<T> ToHashSet<T>(this IQueryable<T> source) => new HashSet<T>();
        public static Task<HashSet<T>> ToHashSetAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(new HashSet<T>());
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
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }

    public static class GroupingExtensions
    {
        public static Task<bool> AllAsync<TKey, T>(this IGrouping<TKey, T> source, Func<T, bool> predicate) =>
            Task.FromResult(true);
        public static Task<bool> AnyAsync<TKey, T>(this IGrouping<TKey, T> source) =>
            Task.FromResult(true);
        public static Task<int> SumAsync<TKey>(this IGrouping<TKey, User> source, Func<User, int> selector) =>
            Task.FromResult(0);
        public static Task<double> AverageAsync<TKey>(this IGrouping<TKey, User> source, Func<User, int> selector) =>
            Task.FromResult(0d);
        public static Task<int> MinAsync<TKey>(this IGrouping<TKey, User> source, Func<User, int> selector) =>
            Task.FromResult(0);
        public static Task<int> MaxAsync<TKey>(this IGrouping<TKey, User> source, Func<User, int> selector) =>
            Task.FromResult(0);
        public static Task<int> CountAsync<TKey, T>(this IGrouping<TKey, T> source) =>
            Task.FromResult(0);
        public static Task<long> LongCountAsync<TKey, T>(this IGrouping<TKey, T> source) =>
            Task.FromResult(0L);
        public static int ExecuteUpdate<TKey, T>(this IGrouping<TKey, T> source) => 0;
        public static Task<int> ExecuteUpdateAsync<TKey, T>(this IGrouping<TKey, T> source) =>
            Task.FromResult(0);
    }
}
";

    private static string Program(string body, bool asyncMethod = false)
    {
        var signature = asyncMethod
            ? "public async Task<object> Run(AppDbContext db, List<int> ids)"
            : "public object Run(AppDbContext db, List<int> ids)";

        return Usings + EfCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        " + signature + @"
        {
            " + body + @"
        }
    }
}";
    }

    private static Task VerifyAsync(string body, bool asyncMethod = false) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body, asyncMethod));

    [Fact]
    public Task ToArrayAsync_FromDbSet_Reports() =>
        VerifyAsync("return {|LC031:db.Users.ToArrayAsync()|};", asyncMethod: true);

    [Fact]
    public Task ToDictionary_FromDbSet_Reports() =>
        VerifyAsync("return {|LC031:db.Users.ToDictionary(u => u.Id)|};");

    [Fact]
    public Task ToDictionaryAsync_FromDbSet_Reports() =>
        VerifyAsync("return {|LC031:db.Users.ToDictionaryAsync(u => u.Id)|};", asyncMethod: true);

    [Fact]
    public Task ToHashSet_FromDbSet_Reports() =>
        VerifyAsync("return {|LC031:db.Users.ToHashSet()|};");

    [Fact]
    public Task ToHashSetAsync_FromDbSet_Reports() =>
        VerifyAsync("return {|LC031:db.Users.ToHashSetAsync()|};", asyncMethod: true);

    [Fact]
    public Task TakeWhile_ThenToList_StaysQuiet() =>
        VerifyAsync("return db.Users.TakeWhile(u => u.IsActive).ToList();");

    [Fact]
    public Task All_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, All = g.All(u => u.IsActive) }).ToList();");

    [Fact]
    public Task Any_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Any = g.Any() }).ToList();");

    [Fact]
    public Task Sum_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Sum = g.Sum(u => u.Id) }).ToList();");

    [Fact]
    public Task Average_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Average = g.Average(u => u.Id) }).ToList();");

    [Fact]
    public Task Min_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Min = g.Min(u => u.Id) }).ToList();");

    [Fact]
    public Task LongCount_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.LongCount() }).ToList();");

    [Fact]
    public Task AllAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, All = g.AllAsync(u => u.IsActive) }).ToList();");

    [Fact]
    public Task AnyAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Any = g.AnyAsync() }).ToList();");

    [Fact]
    public Task SumAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Sum = g.SumAsync(u => u.Id) }).ToList();");

    [Fact]
    public Task AverageAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Average = g.AverageAsync(u => u.Id) }).ToList();");

    [Fact]
    public Task MinAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Min = g.MinAsync(u => u.Id) }).ToList();");

    [Fact]
    public Task MaxAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Max = g.MaxAsync(u => u.Id) }).ToList();");

    [Fact]
    public Task CountAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.CountAsync() }).ToList();");

    [Fact]
    public Task LongCountAsync_KeyedGroup_StaysQuiet() =>
        VerifyAsync("return db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.LongCountAsync() }).ToList();");

    [Fact]
    public Task ExecuteUpdate_KeyedGroup_StillReports() =>
        VerifyAsync("return {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => g.ExecuteUpdate()).ToList()|};");

    [Fact]
    public Task ExecuteUpdateAsync_KeyedGroup_StillReports() =>
        VerifyAsync("return {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => g.ExecuteUpdateAsync()).ToList()|};");
}
