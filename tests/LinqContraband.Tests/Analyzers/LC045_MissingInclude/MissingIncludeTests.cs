using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public class MissingIncludeTests
{
    // LC045 carries the materializer invocation as an additional location for the fixer; the
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
}
