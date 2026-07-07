using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeFixerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TestNamespace;
";

    private const string UsingsWithoutEfCore = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestNamespace;
";

    private const string UsingsWithEfCoreAppended = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestNamespace;
using Microsoft.EntityFrameworkCore;
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
        public DbSet<T> Set<T>() where T : class => null;
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
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
    }
}

namespace TestNamespace
{
    using Microsoft.EntityFrameworkCore;

    public class Order
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public Customer Customer { get; set; }
        public Customer @event { get; set; }
        public List<OrderItem> Items { get; set; }
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

    public class MyDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }

    public class SetOnlyDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }
}";

    [Fact]
    public async Task FixCrime_AddsIncludeBeforeMaterializer()
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
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AppendsAfterExistingInclude()
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
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Items.Count);
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_NestedPath_AddsIncludeThenInclude()
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
            Console.WriteLine({|LC045:o.Customer.Address|}.City);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).Include(x => x.Customer).ThenInclude(x => x.Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AddsMissingEfCoreUsing()
    {
        var test = UsingsWithoutEfCore + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = UsingsWithEfCoreAppended + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_DbContextSetRoot_AddsIncludeBeforeMaterializer()
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
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Set<Order>().Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_DbContextSetRootWithoutDbSetProperty_AddsIncludeBeforeMaterializer()
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
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new SetOnlyDbContext();
        var orders = db.Set<Order>().Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_TwoMissingNavsOnOneQuery_FixesBoth()
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

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).Include(x => x.Items).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
            Console.WriteLine(o.Items.Count);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 2
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Customer", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithLocation(1)
                .WithArguments("Items", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));

        await testObj.RunAsync();
    }

}
