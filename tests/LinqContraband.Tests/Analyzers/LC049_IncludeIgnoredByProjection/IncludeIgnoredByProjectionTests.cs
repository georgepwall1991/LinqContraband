using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection.IncludeIgnoredByProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC049_IncludeIgnoredByProjection;

public class IncludeIgnoredByProjectionTests
{
    internal const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity>
    {
    }

    public sealed class IncludableQueryable<TEntity, TPreviousProperty> : IIncludableQueryable<TEntity, TPreviousProperty> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath) where TEntity : class => new IncludableQueryable<TEntity, TProperty>();
        public static IQueryable<TEntity> Include<TEntity>(this IQueryable<TEntity> source, string navigationPropertyPath) where TEntity : class => source;
        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source, Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) where TEntity : class => new IncludableQueryable<TEntity, TProperty>();
        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(this IIncludableQueryable<TEntity, TPreviousProperty> source, Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) where TEntity : class => new IncludableQueryable<TEntity, TProperty>();
        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> TagWith<TEntity>(this IQueryable<TEntity> source, string tag) => source;
        public static List<TSource> ToList<TSource>(this IQueryable<TSource> source) => new List<TSource>();
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
    }
}

namespace TestApp
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Address Address { get; set; }
    }

    public class Address
    {
        public int Id { get; set; }
        public string City { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Sku { get; set; }
    }

    public class OrderLine
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public Product Product { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public decimal Total { get; set; }
        public DateTime PlacedAt { get; set; }
        public Customer Customer { get; set; }
        public List<OrderLine> Lines { get; set; }
        public Order Parent { get; set; }
    }

    public class OrderSummary
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public CustomerSummary Customer { get; set; }
        public List<LineSummary> Lines { get; set; }
    }

    public class CustomerSummary
    {
        public string Name { get; set; }
    }

    public class LineSummary
    {
        public string Sku { get; set; }
    }

    public class OrderRow
    {
        public OrderRow(int id, string customerName) { }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
    }
}
";

    private static string Wrap(string body) => EFCoreMock + @"
namespace TestApp
{
    class Program
    {
        void Run(AppDbContext db)
        {
" + body + @"
        }

        static OrderSummary Map(Order order) => new OrderSummary();
    }
}";

    [Fact]
    public async Task AnonymousScalarProjection_ReportsInclude()
    {
        var test = Wrap(@"
            var rows = db.Orders
                .{|LC049:Include|}(o => o.Customer)
                .Select(o => new { o.Id, CustomerName = o.Customer.Name })
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Message_NamesIncludeAndEntity()
    {
        var test = Wrap(@"
            var ids = db.Orders.Include(o => o.Customer).Select(o => o.Id).ToList();");

        var expected = VerifyCS.Diagnostic("LC049")
            .WithSpan(128, 33, 128, 40)
            .WithArguments("Include", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DtoProjectionWithNestedDtos_ReportsEveryInclude()
    {
        var test = Wrap(@"
            var rows = db.Orders
                .{|LC049:Include|}(o => o.Customer)
                .{|LC049:Include|}(o => o.Lines).ThenInclude(l => l.Product)
                .Where(o => o.Total > 10)
                .OrderBy(o => o.PlacedAt)
                .Select(o => new OrderSummary
                {
                    Id = o.Id,
                    CustomerName = o.Customer != null ? o.Customer.Name : null,
                    Customer = new CustomerSummary { Name = o.Customer.Name },
                    Lines = o.Lines.Select(l => new LineSummary { Sku = l.Product.Sku }).ToList()
                })
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RecordConstructorProjection_ReportsInclude()
    {
        var test = Wrap(@"
            var rows = db.Orders
                .AsNoTracking()
                .{|LC049:Include|}(o => o.Customer)
                .TagWith(""rows"")
                .AsSplitQuery()
                .Select(o => new OrderRow(o.Id, o.Customer.Name))
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringInclude_Reports()
    {
        var test = Wrap(@"
            var rows = db.Orders.{|LC049:Include|}(""Lines"").Select(o => new { o.Id, Count = o.Lines.Count() }).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ScalarAggregateOverCollection_Reports()
    {
        var test = Wrap(@"
            var totals = db.Orders
                .{|LC049:Include|}(o => o.Lines)
                .Select(o => o.Lines.Sum(l => l.Quantity))
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoSelect_DoesNotReport()
    {
        var test = Wrap(@"
            var orders = db.Orders.Include(o => o.Customer).Where(o => o.Total > 10).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IdentityProjection_DoesNotReport()
    {
        var test = Wrap(@"
            var orders = db.Orders.Include(o => o.Customer).Select(o => o).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntityInsideAnonymousProjection_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders.Include(o => o.Customer).Select(o => new { Order = o, o.Total }).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ProjectedNavigationEntity_DoesNotReport()
    {
        // EF Core carries Include(o => o.Customer).ThenInclude(c => c.Address) onto a projected o.Customer.
        var test = Wrap(@"
            var customers = db.Orders
                .Include(o => o.Customer).ThenInclude(c => c.Address)
                .Select(o => o.Customer)
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ProjectedNavigationCollection_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders
                .Include(o => o.Lines).ThenInclude(l => l.Product)
                .Select(o => new { o.Id, o.Lines })
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CollectionProjectedToEntities_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders
                .Include(o => o.Lines).ThenInclude(l => l.Product)
                .Select(o => new { o.Id, Products = o.Lines.Select(l => l.Product) })
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntityPassedToMethod_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders.Include(o => o.Customer).Select(o => Map(o)).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameTypeNavigationProjection_DoesNotReport()
    {
        var test = Wrap(@"
            var parents = db.Orders.Include(o => o.Customer).Select(o => o.Parent).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectAfterAsEnumerable_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders.Include(o => o.Customer).AsEnumerable().Select(o => o.Customer.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnknownOperatorBetweenIncludeAndSelect_DoesNotReport()
    {
        var test = Wrap(@"
            var rows = db.Orders.Include(o => o.Customer).GroupBy(o => o.Id).Select(g => g.Key).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IncludeStoredInLocal_DoesNotReport()
    {
        var test = Wrap(@"
            var query = db.Orders.Include(o => o.Customer);
            var names = query.Select(o => o.Customer.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeInclude_DoesNotReport()
    {
        var test = EFCoreMock + @"
namespace Other
{
    using TestApp;

    static class Extensions
    {
        public static IQueryable<T> Include<T>(this IQueryable<T> source, Expression<Func<T, object>> path) => source;
    }

    class Program
    {
        void Run(IQueryable<Order> orders)
        {
            var ids = Extensions.Include(orders, o => o.Customer).Select(o => o.Id).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticIncludeCall_Reports()
    {
        var test = Wrap(@"
            var ids = EntityFrameworkQueryableExtensions.{|LC049:Include|}(db.Orders, o => o.Customer).Select(o => o.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxProjection_Reports()
    {
        var test = Wrap(@"
            var names = (from o in db.Orders.{|LC049:Include|}(o => o.Customer)
                         select o.Customer.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxSelectingEntity_DoesNotReport()
    {
        var test = Wrap(@"
            var orders = (from o in db.Orders.Include(o => o.Customer)
                          where o.Total > 10
                          select o).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
