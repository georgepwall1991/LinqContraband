using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// `await foreach` over an EF Core query is the async spelling of the loop LC045 already
/// analyses: the entities are materialized one at a time, and a navigation read on the loop
/// variable has exactly the same failure modes. These cases pin that the async spelling is
/// analysed rather than skipped.
/// </summary>
public partial class MissingIncludeAsyncEnumerationTests
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

    // A self-contained mock: this file is the only LC045 suite whose DbSet must also be an
    // IAsyncEnumerable, and the shared mock cannot carry that without making Where/Select
    // ambiguous against System.Linq.AsyncEnumerable on current reference assemblies.
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

    // The real DbSet<T> is both an IQueryable<T> and an IAsyncEnumerable<T>, which is what
    // makes `await foreach (var x in db.Set)` compile without AsAsyncEnumerable().
    public class DbSet<T> : IQueryable<T>, IAsyncEnumerable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public IAsyncEnumerator<T> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source,
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
    }

    public static class AsyncEnumerableLookalike
    {
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(IQueryable<T> source) => null;
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
        public Product Product { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Product> Products { get; set; }
    }
}
";

    private static DiagnosticResult Diagnostic(int markupKey, string navigationPath, string entityName)
    {
        return Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
                LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>
            .Diagnostic("LC045")
            .WithLocation(markupKey)
            .WithArguments(navigationPath, entityName)
            .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations);
    }

    private static async Task VerifyAsync(string programBody, params DiagnosticResult[] expected)
    {
        var test =
            Usings
            + @"
class Program
{
"
            + programBody
            + @"
}
"
            + MockNamespace;

        var analyzerTest = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
        >
        {
            TestCode = test,
            // IAsyncEnumerable<T> is not in the default reference set.
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        analyzerTest.ExpectedDiagnostics.AddRange(expected);
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task TestCrime_AwaitForeachOverAsAsyncEnumerable_Reports()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.AsAsyncEnumerable())
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AwaitForeachOverFilteredQuery_Reports()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Where(o => o.Id > 0).AsNoTracking().AsAsyncEnumerable())
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AwaitForeachNestedCollectionIteration_ReportsFullPath()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Include(o => o.Items).AsAsyncEnumerable())
        {
            foreach (var item in order.Items)
            {
                Console.WriteLine({|#0:item.Product|}.Name);
            }
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachWithInclude_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Include(o => o.Customer).AsAsyncEnumerable())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachOverNonEntityStream_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main(IAsyncEnumerable<Order> stream)
    {
        await foreach (var order in stream)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachEscapedEntity_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.AsAsyncEnumerable())
        {
            Handle(order);
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Handle(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachOverProjection_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Select(o => o).AsAsyncEnumerable())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_AwaitForeachOverDbSetDirectly_Reports()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachOverLookalikeAsAsyncEnumerable_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in AsyncEnumerableLookalike.AsAsyncEnumerable(db.Orders))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachNavigationWrite_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main(Customer customer)
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.AsAsyncEnumerable())
        {
            order.Customer = customer;
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AwaitForeachScalarOnly_StaysQuiet()
    {
        await VerifyAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.AsAsyncEnumerable())
        {
            Console.WriteLine(order.Status);
        }
    }
"
        );
    }

    private static async Task VerifyFixAsync(string programBody, string fixedProgramBody)
    {
        var codeFixTest = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeFixer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
        >
        {
            TestCode = Usings + "\nclass Program\n{\n" + programBody + "\n}\n" + MockNamespace,
            FixedCode = Usings + "\nclass Program\n{\n" + fixedProgramBody + "\n}\n" + MockNamespace,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        await codeFixTest.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AwaitForeachOverAsAsyncEnumerable_AddsIncludeBeforeTheBridge()
    {
        await VerifyFixAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.AsAsyncEnumerable())
        {
            Console.WriteLine({|LC045:order.Customer|}.Name);
        }
    }
",
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Include(x => x.Customer).AsAsyncEnumerable())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task FixCrime_AwaitForeachOverDbSetDirectly_WrapsTheLoopSource()
    {
        await VerifyFixAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders)
        {
            Console.WriteLine({|LC045:order.Customer|}.Name);
        }
    }
",
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders.Include(x => x.Customer).AsAsyncEnumerable())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task FixInnocent_AwaitForeachOverDbSetDirectly_WithoutTheBridge_OffersNoFix()
    {
        // EF Core without AsAsyncEnumerable leaves no way to wrap the source and keep the loop
        // compiling, so LC045 must report and withhold the fix rather than break the build.
        var mockWithoutBridge = MockNamespace.Replace(
            "public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;",
            string.Empty);
        var mockWithoutLookalike = mockWithoutBridge.Replace(
            "public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(IQueryable<T> source) => null;",
            string.Empty);
        const string body = @"
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var order in db.Orders)
        {
            Console.WriteLine({|LC045:order.Customer|}.Name);
        }
    }
";
        var source = Usings + "\nclass Program\n{\n" + body + "\n}\n" + mockWithoutLookalike;

        var codeFixTest = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeFixer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
        >
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        await codeFixTest.RunAsync();
    }
}
