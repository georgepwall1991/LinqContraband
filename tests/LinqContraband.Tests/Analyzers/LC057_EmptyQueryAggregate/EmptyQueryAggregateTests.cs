using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC057_EmptyQueryAggregate.EmptyQueryAggregateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC057_EmptyQueryAggregate;

public class EmptyQueryAggregateTests
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

    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }

    public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;
        public static Task<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => null;
        public static Task<TSource> MaxAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TResult> MaxAsync<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<TSource> MinAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TResult> MinAsync<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<decimal> AverageAsync(this IQueryable<decimal> source, CancellationToken cancellationToken = default) => null;
        public static Task<decimal?> AverageAsync(this IQueryable<decimal?> source, CancellationToken cancellationToken = default) => null;
        public static Task<double> AverageAsync(this IQueryable<int> source, CancellationToken cancellationToken = default) => null;
        public static Task<double?> AverageAsync(this IQueryable<int?> source, CancellationToken cancellationToken = default) => null;
        public static Task<decimal> AverageAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, decimal>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<decimal?> AverageAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, decimal?>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<double> AverageAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, int>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<double?> AverageAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, int?>> selector, CancellationToken cancellationToken = default) => null;
        public static Task<decimal> SumAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, decimal>> selector, CancellationToken cancellationToken = default) => null;
    }
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal? Discount { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
}

public class Category
{
    public int Id { get; set; }
}

public class ShopContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
}

public class ProductRepository
{
    private readonly ShopContext _db = new ShopContext();

    public IQueryable<Product> Query() => _db.Products;

    public IQueryable<Product> Active()
    {
        return _db.Products.Where(p => p.Price > 0);
    }

    public virtual IQueryable<Product> Overridable() => _db.Products;
}

public interface IProductRepository
{
    IQueryable<Product> Query();
}

public static class ProductQueries
{
    public static IQueryable<Product> InCategory(this IQueryable<Product> source, int categoryId) =>
        source.Where(p => p.CategoryId == categoryId);
}
";

    internal static string Wrap(string body) => Usings + @"
class Program
{
    private IQueryable<Product> _cached;

    async Task<object> Run(ShopContext db, ProductRepository repo, IProductRepository contract, IQueryable<Product> products, int categoryId, CancellationToken ct)
    {
" + body + @"
        return null;
    }
}
" + EfMock;

    [Theory]
    [InlineData(@"var max = db.Products.{|#0:Max|}(p => p.Price);", "Max", "decimal")]
    [InlineData(@"var min = db.Products.{|#0:Min|}(p => p.Created);", "Min", "DateTime")]
    [InlineData(@"var average = db.Products.{|#0:Average|}(p => p.Price);", "Average", "decimal")]
    [InlineData(@"var average = db.Products.{|#0:Average|}(p => p.CategoryId);", "Average", "int")]
    [InlineData(@"var max = await db.Products.{|#0:MaxAsync|}(p => p.Price, ct);", "MaxAsync", "decimal")]
    [InlineData(@"var min = await db.Products.{|#0:MinAsync|}(p => p.Id);", "MinAsync", "int")]
    [InlineData(@"var average = await db.Products.{|#0:AverageAsync|}(p => p.CategoryId, ct);", "AverageAsync", "int")]
    [InlineData(@"var max = db.Products.Select(p => p.Price).{|#0:Max|}();", "Max", "decimal")]
    [InlineData(@"var max = await db.Products.Select(p => p.Id).{|#0:MaxAsync|}(ct);", "MaxAsync", "int")]
    [InlineData(@"var average = await db.Products.Select(p => p.Price).{|#0:AverageAsync|}();", "AverageAsync", "decimal")]
    [InlineData(@"var max = db.Set<Product>().Where(p => p.CategoryId == categoryId).{|#0:Max|}(p => p.Price);", "Max", "decimal")]
    [InlineData(@"var max = db.Products.AsNoTracking().{|#0:Max|}(p => p.Price);", "Max", "decimal")]
    [InlineData(@"var max = Queryable.{|#0:Max|}(db.Products, p => p.Price);", "Max", "decimal")]
    public async Task AggregateOverNonNullableValue_Reports(string body, string method, string type)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0).WithArguments(method, type));
    }

    [Theory]
    // A query local, including one that is recomposed.
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price);")]
    [InlineData(@"var query = db.Products.AsQueryable(); query = query.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price);")]
    [InlineData(@"IQueryable<Product> query = db.Products; if (categoryId > 0) query = query.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price);")]
    // Repository and query helpers declared in the project.
    [InlineData(@"var max = repo.Query().{|LC057:Max|}(p => p.Price);")]
    [InlineData(@"var max = repo.Active().{|LC057:Max|}(p => p.Price);")]
    [InlineData(@"var max = db.Products.InCategory(categoryId).{|LC057:Max|}(p => p.Price);")]
    // The guard comes after the aggregate.
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price); if (query.Any()) { }")]
    // A guard on a different query proves nothing about this one.
    [InlineData(@"if (db.Categories.Any()) { var max = db.Products.{|LC057:Max|}(p => p.Price); }")]
    // Cast to a non-nullable type is still non-nullable.
    [InlineData(@"var max = db.Products.{|LC057:Max|}(p => (long)p.Id);")]
    public async Task EfQueryShapes_Report(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Theory]
    // Nullable selectors return null on an empty query.
    [InlineData(@"var max = db.Products.Max(p => (decimal?)p.Price);")]
    [InlineData(@"var max = db.Products.Max(p => p.Discount);")]
    [InlineData(@"var average = await db.Products.AverageAsync(p => (int?)p.CategoryId, ct);")]
    [InlineData(@"var max = db.Products.Select(p => (decimal?)p.Price).Max();")]
    // Reference types are null on an empty query.
    [InlineData(@"var max = db.Products.Max(p => p.Name);")]
    // Sum returns zero.
    [InlineData(@"var sum = db.Products.Sum(p => p.Price);")]
    [InlineData(@"var sum = await db.Products.SumAsync(p => p.Price, ct);")]
    // Unknown source: an IQueryable parameter, field, interface or overridable helper.
    [InlineData(@"var max = products.Max(p => p.Price);")]
    [InlineData(@"var max = products.Where(p => p.CategoryId == categoryId).Max(p => p.Price);")]
    [InlineData(@"var max = _cached.Max(p => p.Price);")]
    [InlineData(@"var max = contract.Query().Max(p => p.Price);")]
    [InlineData(@"var max = repo.Overridable().Max(p => p.Price);")]
    [InlineData(@"var query = db.Products.AsQueryable(); query = products; var max = query.Max(p => p.Price);")]
    // In memory.
    [InlineData(@"var max = new List<Product>().AsQueryable().Max(p => p.Price);")]
    [InlineData(@"var max = db.Products.ToList().Max(p => p.Price);")]
    [InlineData(@"var max = db.Products.AsEnumerable().Max(p => p.Price);")]
    // Guarded by Any or Count on the same query.
    [InlineData(@"if (db.Products.Any()) { var max = db.Products.Max(p => p.Price); }")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); if (!query.Any()) return null; var max = query.Max(p => p.Price);")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); if (query.Count() == 0) return null; var max = query.Max(p => p.Price);")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); if (!await query.AnyAsync(ct)) return null; var max = await query.MaxAsync(p => p.Price, ct);")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); var count = await query.CountAsync(ct); if (count == 0) return null; var max = query.Max(p => p.Price);")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); var max = query.Any() ? query.Max(p => p.Price) : 0m;")]
    [InlineData(@"var query = db.Products.Where(p => p.CategoryId == categoryId); var high = query.Any(p => p.Price > 10) && query.Max(p => p.Price) > 100;")]
    // Groups are never empty.
    [InlineData(@"var rows = db.Products.GroupBy(p => p.CategoryId).Select(g => new { g.Key, Max = g.Max(p => p.Price) }).ToList();")]
    // Inside another query's expression tree.
    [InlineData(@"var rows = db.Categories.Select(c => new { c.Id, Max = db.Products.Max(p => p.Price) }).ToList();")]
    public async Task SafeOrUnknownShapes_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task EnumerableAggregateOverMaterializedList_DoesNotReport()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var prices = new List<decimal> { 1m, 2m };
        var max = prices.Max();"));
    }
}
