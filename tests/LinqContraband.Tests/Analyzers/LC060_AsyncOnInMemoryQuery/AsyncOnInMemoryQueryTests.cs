using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery.AsyncOnInMemoryQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC060_AsyncOnInMemoryQuery;

public class AsyncOnInMemoryQueryTests
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
using MockQueryable;
";

    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }

    public abstract class DbSet<TEntity> : IQueryable<TEntity>, IAsyncEnumerable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
        public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> Include<T, TProperty>(this IQueryable<T> source, Expression<Func<T, TProperty>> navigationPropertyPath) where T : class => source;
        public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => source;
        public static string ToQueryString(this IQueryable source) => null;
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<HashSet<T>> ToHashSetAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> source, Func<T, TKey> keySelector, CancellationToken cancellationToken = default) where TKey : notnull => null;
        public static Task<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default) where TKey : notnull => null;
        public static Task<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<T> SingleOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<T> LastOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<bool> AnyAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<bool> AllAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<bool> ContainsAsync<T>(this IQueryable<T> source, T item, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<long> LongCountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<decimal> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<TResult> MaxAsync<T, TResult>(this IQueryable<T> source, Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) => null;
        public static Task ForEachAsync<T>(this IQueryable<T> source, Action<T> action, CancellationToken cancellationToken = default) => null;
        public static Task LoadAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
    }
}

namespace MockQueryable
{
    public static class MockQueryableExtensions
    {
        public static IQueryable<T> BuildMock<T>(this IEnumerable<T> data) where T : class => null;
        public static IQueryable<T> BuildMock<T>(this IQueryable<T> data) where T : class => null;
    }
}

namespace Custom
{
    public static class MyAsyncExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => Task.FromResult(source.ToList());
    }
}

public class Item
{
    public int Id { get; set; }
    public bool Active { get; set; }
    public decimal Price { get; set; }
    public Item Parent { get; set; }
}

public class ShopContext : DbContext
{
    public DbSet<Item> Items { get; set; }
}

/// <summary>An in-memory collection that can also be enumerated asynchronously.</summary>
public class AsyncList<T> : List<T>, IAsyncEnumerable<T>
{
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;
}

public static class TestData
{
    public static IQueryable<Item> Items() => new List<Item> { new Item() }.AsQueryable();

    public static IQueryable<Item> ItemsFromArray()
    {
        var items = new[] { new Item() };
        return items.AsQueryable().Where(i => i.Active);
    }

    public static IQueryable<Item> Active(IQueryable<Item> source) => source.Where(i => i.Active);

    public static IQueryable<Item> ActiveOnly(this IQueryable<Item> source) => source.Where(i => i.Active);

    public static IQueryable<Item> Wrapped(IEnumerable<Item> source) => source.AsQueryable();
}

public class Repository
{
    public virtual IQueryable<Item> Query() => new List<Item>().AsQueryable();
}
";

    internal static string Wrap(string body) => Usings + @"
class Program
{
    private IQueryable<Item> _query;
    private List<Item> _list = new List<Item>();

    async Task<object> Run(ShopContext db, List<Item> list, Item[] array, IEnumerable<Item> sequence, IQueryable<Item> query, Repository repo, CancellationToken ct)
    {
" + body + @"
        return null;
    }
}
" + EfMock;

    [Theory]
    // The chain starts at AsQueryable() over an in-memory collection.
    [InlineData(@"var items = await list.AsQueryable().Where(x => x.Active).{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await list.AsQueryable().{|#0:ToListAsync|}();", "ToListAsync")]
    [InlineData(@"var count = await array.AsQueryable().{|#0:CountAsync|}(ct);", "CountAsync")]
    [InlineData(@"var item = await list.AsQueryable().{|#0:FirstOrDefaultAsync|}(x => x.Active, ct);", "FirstOrDefaultAsync")]
    [InlineData(@"var any = await _list.AsQueryable().{|#0:AnyAsync|}(ct);", "AnyAsync")]
    [InlineData(@"var total = await list.AsQueryable().{|#0:SumAsync|}(x => x.Price, ct);", "SumAsync")]
    [InlineData(@"var byId = await list.AsQueryable().{|#0:ToDictionaryAsync|}(x => x.Id, ct);", "ToDictionaryAsync")]
    [InlineData(@"var items = await list.AsQueryable().{|#0:ToArrayAsync|}(ct);", "ToArrayAsync")]
    [InlineData(@"await list.AsQueryable().{|#0:ForEachAsync|}(x => x.Active = false, ct);", "ForEachAsync")]
    [InlineData(@"await list.AsQueryable().{|#0:LoadAsync|}(ct);", "LoadAsync")]
    [InlineData(@"await foreach (var x in list.AsQueryable().{|#0:AsAsyncEnumerable|}()) { }", "AsAsyncEnumerable")]
    [InlineData(@"var task = list.AsQueryable().{|#0:ToListAsync|}(ct); var items = await task;", "ToListAsync")]
    // Through Queryable operators and EF Core operators that return an in-memory source unchanged.
    [InlineData(@"var items = await list.AsQueryable().OrderBy(x => x.Id).Skip(1).Take(5).Select(x => x.Id).{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await list.AsQueryable().AsNoTracking().Include(x => x.Parent).TagWith(""t"").{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await Queryable.AsQueryable(list).{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await EntityFrameworkQueryableExtensions.{|#0:ToListAsync|}(list.AsQueryable(), ct);", "ToListAsync")]
    [InlineData(@"var items = await list.AsQueryable().AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await new List<Item>().AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await new EnumerableQuery<Item>(list).{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await ((IEnumerable<Item>)list).AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    // LINQ to Objects operators never hand back an IQueryable, so AsQueryable() wraps them in memory.
    [InlineData(@"var items = await list.Where(x => x.Active).AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await sequence.Select(x => x).AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    [InlineData(@"var items = await sequence.ToList().AsQueryable().{|#0:ToListAsync|}(ct);", "ToListAsync")]
    public async Task AsyncOperatorOnInMemoryQuery_Reports(string body, string method)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0).WithArguments(method));
    }

    [Theory]
    // A local whose every write is an in-memory query, including one that is recomposed.
    [InlineData(@"var q = list.AsQueryable(); var items = await q.{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"var q = list.AsQueryable(); q = q.Where(x => x.Active); if (ct.CanBeCanceled) q = q.OrderBy(x => x.Id); var items = await q.{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"IQueryable<Item> q = ct.CanBeCanceled ? list.AsQueryable() : array.AsQueryable(); var count = await q.{|LC060:CountAsync|}(ct);")]
    [InlineData(@"IQueryable<Item> q; q = array.AsQueryable(); var count = await q.{|LC060:CountAsync|}(ct);")]
    // Same-project helpers that are proven to build an in-memory query.
    [InlineData(@"var items = await TestData.Items().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"var items = await TestData.ItemsFromArray().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"var items = await TestData.Active(list.AsQueryable()).{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"var items = await list.AsQueryable().ActiveOnly().{|LC060:ToListAsync|}(ct);")]
    public async Task ProvenInMemoryThroughLocalsAndHelpers_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Theory]
    // EF Core queries.
    [InlineData(@"var items = await db.Items.Where(x => x.Active).ToListAsync(ct);")]
    [InlineData(@"var items = await db.Set<Item>().ToListAsync(ct);")]
    // AsQueryable() over something already queryable is a no-op cast.
    [InlineData(@"var items = await db.Items.AsQueryable().ToListAsync(ct);")]
    [InlineData(@"var items = await query.AsQueryable().ToListAsync(ct);")]
    [InlineData(@"var items = await ((IEnumerable<Item>)db.Items).AsQueryable().ToListAsync(ct);")]
    // Queries of unknown provenance.
    [InlineData(@"var items = await query.Where(x => x.Active).ToListAsync(ct);")]
    [InlineData(@"var items = await _query.ToListAsync(ct);")]
    [InlineData(@"var items = await repo.Query().ToListAsync(ct);")]
    [InlineData(@"var q = query; var items = await q.ToListAsync(ct);")]
    [InlineData(@"var q = list.AsQueryable(); if (ct.CanBeCanceled) q = db.Items; var items = await q.ToListAsync(ct);")]
    [InlineData(@"var items = await TestData.Active(query).ToListAsync(ct);")]
    // An interface-typed sequence may be a DbSet at run time.
    [InlineData(@"var items = await sequence.AsQueryable().ToListAsync(ct);")]
    [InlineData(@"var items = await TestData.Wrapped(list).ToListAsync(ct);")]
    [InlineData(@"IEnumerable<Item> items = list; if (ct.CanBeCanceled) items = sequence; var result = await items.AsQueryable().ToListAsync(ct);")]
    [InlineData(@"IEnumerable<Item> items = db.Items; var result = await items.AsQueryable().ToListAsync(ct);")]
    // AsEnumerable() and Cast() hand back their source, which may be a DbSet.
    [InlineData(@"var items = await db.Items.AsEnumerable().AsQueryable().ToListAsync(ct);")]
    [InlineData(@"var items = await sequence.Cast<Item>().AsQueryable().ToListAsync(ct);")]
    // Sources that can be enumerated asynchronously (MockQueryable, custom async collections).
    [InlineData(@"var items = await list.BuildMock().ToListAsync(ct);")]
    [InlineData(@"var items = await list.AsQueryable().BuildMock().ToListAsync(ct);")]
    [InlineData(@"var items = await new AsyncList<Item>().AsQueryable().ToListAsync(ct);")]
    // Synchronous operators and non-executing EF Core calls on an in-memory query are fine.
    [InlineData(@"var items = list.AsQueryable().Where(x => x.Active).ToList();")]
    [InlineData(@"var count = list.AsQueryable().Count();")]
    [InlineData(@"var sql = list.AsQueryable().ToQueryString();")]
    [InlineData(@"var q = list.AsQueryable().AsNoTracking();")]
    // Another library's ToListAsync that handles in-memory queries.
    [InlineData(@"var items = await Custom.MyAsyncExtensions.ToListAsync(list.AsQueryable(), ct);")]
    public async Task UnprovenOrSafeShapes_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task AsyncLambdaAndLocalFunction_Report()
    {
        var body = @"
        Func<Task<List<Item>>> load = async () => await list.AsQueryable().{|LC060:ToListAsync|}(ct);
        async Task<int> CountAsync() => await array.AsQueryable().{|LC060:CountAsync|}(ct);";
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task CompilationWithoutEfCore_DoesNotReport()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public static class MyExtensions
{
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => Task.FromResult(source.ToList());
}

class Program
{
    async Task Run(List<int> list)
    {
        var items = await list.AsQueryable().ToListAsync();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
