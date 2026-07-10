using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeTests
{
    // LC045 carries the exact query source expression as an additional location for the fixer; the
    // fixer tests verify it lands correctly, so analyzer tests ignore it instead of spelling
    // out a second brittle span per case.
    private static DiagnosticResult Diagnostic(int markupKey, string navigationPath, string entityName)
    {
        return VerifyCS.Diagnostic("LC045")
            .WithLocation(markupKey)
            .WithArguments(navigationPath, entityName)
            .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations);
    }

    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Query;

    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public EntityEntry<T> Entry<T>(T entity) where T : class => null;
        public DbSet<T> Set<T>() where T : class => null;
    }

    public class EntityEntry<T> where T : class { }

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
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
    }
}

namespace TestNamespace
{
    public class Order
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public Customer Customer { get; set; }
        public List<OrderItem> Items { get; set; }
        public OrderSummary Summary { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Address Address { get; set; }
        public void Clear() { }
    }

    public class Address
    {
        public int Id { get; set; }
        public string City { get; set; }
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
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderItemDetail> OrderItemDetails { get; set; }
        public DbSet<Product> Products { get; set; }
    }

    public class SetOnlyDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }

    public class FieldDbContext : DbContext
    {
        public DbSet<Order> Orders = null;
        public DbSet<Customer> Customers = null;
    }
}";

    [Fact]
    public async Task TestCrime_ForeachOverList_ReferenceNavAccessed_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_CollectionNavAccessed_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Items|}.Count);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Items", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_SingleEntityMaterializer_NavAccessedOnLocal_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (order != null)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_PartialInclude_FlagsOnlyTheMissingNav()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Items.Count);
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NestedNavAccess_FlagsTheFullPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer.Address|}.City);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_TwoMissingNavs_TwoDiagnostics()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
            Console.WriteLine({|#1:o.Items|}.Count);
        }
    }
}
" + MockNamespace;

        var expected = new[]
        {
            Diagnostic(0, "Customer", "Order"),
            Diagnostic(1, "Items", "Order")
        };

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_StringIncludeForOtherNav_DoesNotBlindTheRule()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(""Items"").ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_AwaitToListAsync_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var orders = await db.Orders.ToListAsync();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_QueryHoistedToLocal_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var q = db.Orders.Where(o => o.Id > 0);
        var orders = q.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineAccessOnSingleEntityMaterializer_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var name = {|#0:db.Orders.First().Customer|}.Name;
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NullGuardedAccess_StillTriggersDiagnostic()
    {
        // Deliberate: with lazy-loading proxies the null check itself fires the N+1 query;
        // without proxies the navigation is always null and the guard is dead code hiding
        // the missing Include. Either way the query is wrong.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            if ({|#0:o.Customer|} != null)
            {
                Console.WriteLine(o.Customer.Name);
            }
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InConstructorBody_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public Program(MyDbContext db)
    {
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_DirectIndexedAccess_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine({|#0:orders[0].Customer|}.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ReadBeforeNavAssignment_StillTriggersDiagnostic()
    {
        // The assignment only backs reads AFTER it; this read happens first and still hits
        // unloaded data.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Console.WriteLine({|#0:order.Customer|}.Name);
        order.Customer = new Customer();
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_DbSetFieldRoot_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new FieldDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_DbContextSetRoot_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Set<Order>().ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_HoistedDbContextSetQuery_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var query = db.Set<Order>().Where(o => o.Id > 0);
        var orders = query.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_DbContextSetRootWithoutDbSetProperty_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new SetOnlyDbContext();
        var orders = db.Set<Order>().ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_MutatorNamedMethodOnReferenceNav_StillTriggersDiagnostic()
    {
        // Clear() here is an instance method on the Customer entity, not a collection
        // mutation: evaluating o.Customer is still a read of unloaded data.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            {|#0:o.Customer|}.Clear();
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_AsNoTrackingQuery_StillTriggersDiagnostic()
    {
        // AsNoTracking + missing Include is the worst case: even with lazy-loading proxies
        // configured, many setups cannot lazy-load untracked entities — the nav is just null.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.AsNoTracking().ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerForeach_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var order in db.Orders.ToList())
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_DirectDbSetForeach_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var order in db.Orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_HoistedQueryableForeach_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IQueryable<Order> query = db.Orders.Where(order => order.Id > 0);
        foreach (var order in query)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestInnocent_DirectDbSetForeachWithInclude_StaysQuiet()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var order in db.Orders.Include(order => order.Customer))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_NestedCollectionIteration_ReportsMaximalPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                Console.WriteLine({|#0:item.Product|}.Name);
            }
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Items.Product", "Order"));
    }

    [Fact]
    public async Task TestInnocent_NestedCollectionIterationWithFullInclude_StaysQuiet()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(order => order.Items).ThenInclude(item => item.Product).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
