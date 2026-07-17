using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    private static DiagnosticResult Diagnostic(
        int markupKey,
        string navigationPath,
        string entityName
    )
    {
        return VerifyCS
            .Diagnostic("LC045")
            .WithLocation(markupKey)
            .WithArguments(navigationPath, entityName)
            .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations);
    }

    private const string Usings =
        @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TestNamespace;
";

    private const string MockNamespace =
        @"
namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Query;

    public class DbContext : IDisposable
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
        public void Dispose() { }
        public int SaveChanges() => 0;
        public EntityEntry<T> Entry<T>(T entity) where T : class => null;
    }

    public class ModelBuilder
    {
        public Metadata.Builders.EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => null;

        public void ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration)
            where TEntity : class { }
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(Metadata.Builders.EntityTypeBuilder<TEntity> builder);
    }

    namespace Metadata.Builders
    {
        public class EntityTypeBuilder<TEntity> where TEntity : class
        {
            public EntityTypeBuilder<TEntity> HasKey<TProperty>(
                System.Linq.Expressions.Expression<Func<TEntity, TProperty>> keyExpression) => this;

            public EntityTypeBuilder<TEntity> Ignore<TProperty>(
                System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertyExpression) => this;

            public NavigationBuilder<TEntity, TProperty> Navigation<TProperty>(
                System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationExpression) => null;
        }

        public class NavigationBuilder { }

        public class NavigationBuilder<TSource, TTarget> : NavigationBuilder
            where TSource : class
        {
            public virtual NavigationBuilder<TSource, TTarget> AutoInclude(bool autoInclude = true) => this;
        }
    }

    public class EntityEntry<T> where T : class { }

    public static class RelationalEntityTypeBuilderExtensions
    {
        public static Metadata.Builders.EntityTypeBuilder<TEntity> ToTable<TEntity>(
            this Metadata.Builders.EntityTypeBuilder<TEntity> builder,
            string name) where TEntity : class => builder;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TEntity> Include<TEntity>(
            this IQueryable<TEntity> source,
            string navigationPropertyPath)
            => source;

        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source,
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, TPreviousProperty> source,
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreAutoIncludes<T>(this IQueryable<T> source) => source;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
        public static Task<HashSet<T>> ToHashSetAsync<T>(
            this IQueryable<T> source,
            System.Threading.CancellationToken cancellationToken = default) => null;
        public static Task<HashSet<T>> ToHashSetAsync<T>(
            this IQueryable<T> source,
            IEqualityComparer<T> comparer,
            System.Threading.CancellationToken cancellationToken = default) => null;
        public static Task<T> ElementAtAsync<T>(
            this IQueryable<T> source,
            int index,
            System.Threading.CancellationToken cancellationToken = default) => null;
        public static Task<T> ElementAtOrDefaultAsync<T>(
            this IQueryable<T> source,
            int index,
            System.Threading.CancellationToken cancellationToken = default) => null;
        public static Task<T> FirstOrDefaultAsync<T>(
            this IQueryable<T> source,
            System.Linq.Expressions.Expression<Func<T, bool>> predicate,
            System.Threading.CancellationToken cancellationToken = default) => null;
    }
}

namespace TestNamespace
{
    public class OrderBase
    {
        public Customer Customer { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public Customer Customer { get; set; }
        public Customer BillingCustomer { get; set; }
        public List<OrderItem> Items { get; set; }
        public List<OrderItem> OtherItems { get; set; }
        public OrderSummary Summary { get; set; }
    }

    public class SpecialOrder : OrderBase
    {
        public int Id { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Address Address { get; set; }
        public Customer GetDetached() => new Customer { Address = new Address() };
    }

    public class Address
    {
        public int Id { get; set; }
        public string City { get; set; }
        public Region Region { get; set; }
    }

    public class Region
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public Product Product { get; set; }
        public List<OrderItemDetail> Details { get; set; }
    }

    public class OrderItemDetail
    {
        public int Id { get; set; }
        public Product Product { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class OrderSummary
    {
        public decimal Total { get; set; }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<SpecialOrder> SpecialOrders { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderItemDetail> OrderItemDetails { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Region> Regions { get; set; }
    }

    public static class MyExtensions
    {
        public static IQueryable<T> Shuffle<T>(this IQueryable<T> source) => source;
    }
}";

    [Fact]
    public async Task TestInnocent_SelectProjectionInChain_NoDiagnostic()
    {
        // A Select reshapes the query: the materialized objects are no longer raw entities, so
        // any navigation data was either projected deliberately or the access happens on a
        // different type. The whole query is out of scope.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var customers = db.Orders.Select(o => o.Customer).ToList();
        foreach (var c in customers)
        {
            Console.WriteLine(c.Address.City);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_OnlyScalarPropertiesAccessed_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Id + o.Status);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_PropertyTypeWithoutDbSet_NoDiagnostic()
    {
        // OrderSummary has no DbSet on the context: it is an owned/unmapped type, which EF
        // loads with the entity (or never), so Include does not apply.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Summary.Total);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NavAssignedNotRead_NoDiagnostic()
    {
        // Setting a navigation is how relationships are created on tracked entities; it does
        // not read unloaded data.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            o.Customer = new Customer();
        }
        db.SaveChanges();
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CollectionNavMutated_NoDiagnostic()
    {
        // Adding to a tracked entity's collection is a legitimate write pattern that does not
        // require the existing rows to be loaded.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            o.Items.Add(new OrderItem());
        }
        db.SaveChanges();
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NonEfSource_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var source = new List<Order>();
        var orders = source.Where(o => o.Id > 0).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UnknownQueryOperatorInChain_NoDiagnostic()
    {
        // A custom operator may reshape the query or add its own includes; bail conservatively.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Shuffle().ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NavAssignedThenRead_NoDiagnostic()
    {
        // The navigation now points at an in-memory object, so the later read is backed
        // regardless of Include.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order.Customer = new Customer();
        Console.WriteLine(order.Customer.Name);
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NameofNavigation_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(nameof(o.Customer));
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DeconstructionAssignmentToNav_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            (o.Customer, o.Status) = (new Customer(), ""done"");
        }
        db.SaveChanges();
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AggregateTerminal_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var count = db.Orders.Count();
        Console.WriteLine(count);
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
