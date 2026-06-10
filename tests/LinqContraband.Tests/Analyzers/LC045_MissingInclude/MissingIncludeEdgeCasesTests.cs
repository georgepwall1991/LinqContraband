using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public class MissingIncludeEdgeCasesTests
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

    public static class MyExtensions
    {
        public static IQueryable<T> Shuffle<T>(this IQueryable<T> source) => source;
    }
}";

    [Fact]
    public async Task TestInnocent_LambdaIncludeCoversAccess_NoDiagnostic()
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
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_StringIncludeCoversAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(""Customer"").ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ThenIncludeCoversNestedAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ThenInclude(c => c.Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FilteredIncludeCoversAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items.Where(i => i.Id > 0)).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Items.Count);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SelectProjectionInChain_NoDiagnostic()
    {
        // A Select reshapes the query: the materialized objects are no longer raw entities, so
        // any navigation data was either projected deliberately or the access happens on a
        // different type. The whole query is out of scope.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_OnlyScalarPropertiesAccessed_NoDiagnostic()
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
            Console.WriteLine(o.Id + o.Status);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_PropertyTypeWithoutDbSet_NoDiagnostic()
    {
        // OrderSummary has no DbSet on the context: it is an owned/unmapped type, which EF
        // loads with the entity (or never), so Include does not apply.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultLocalReassigned_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders = new List<Order>();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultPassedAsArgument_NoDiagnostic()
    {
        // The helper may explicitly load the navigations; once the list escapes we cannot
        // prove the access is unbacked.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultReturned_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    List<Order> Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine(orders.Count);
        return orders;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultUsedThroughLambda_NoDiagnostic()
    {
        // Accesses inside lambdas over the result are out of scope for v1; the local escaping
        // into the Select call keeps the rule quiet.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(o => o.Customer.Name).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ForEachMethodOnResult_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders.ForEach(o => Console.WriteLine(o.Customer.Name));
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NavAssignedNotRead_NoDiagnostic()
    {
        // Setting a navigation is how relationships are created on tracked entities; it does
        // not read unloaded data.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CollectionNavMutated_NoDiagnostic()
    {
        // Adding to a tracked entity's collection is a legitimate write pattern that does not
        // require the existing rows to be loaded.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityPassedToEntry_NoDiagnostic()
    {
        // db.Entry(order) may be used to explicitly load the navigation; the entity escaping
        // as an argument keeps the rule quiet.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        db.Entry(order);
        Console.WriteLine(order.Customer.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NonEfSource_NoDiagnostic()
    {
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NonConstantStringInclude_BailsOnWholeQuery_NoDiagnostic()
    {
        // We cannot prove what the dynamic Include loads, so the entire query is out of scope
        // — even for navigations the dynamic string could not plausibly cover.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var navigation = ""Customer"";
        var orders = db.Orders.Include(navigation).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
            Console.WriteLine(o.Items.Count);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UnknownQueryOperatorInChain_NoDiagnostic()
    {
        // A custom operator may reshape the query or add its own includes; bail conservatively.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NullForgivingMidPathInclude_NoDiagnostic()
    {
        // o.Customer!.Address is the idiomatic NRT spelling of a multi-level include; the
        // parser must see "Customer.Address", not a truncated "Address".
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer!.Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CastMidPathInclude_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => ((Customer)o.Customer).Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NavAssignedThenRead_NoDiagnostic()
    {
        // The navigation now points at an in-memory object, so the later read is backed
        // regardless of Include.
        var test = Usings + @"
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
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityLocalReusedAcrossObjects_NoDiagnostic()
    {
        // The local is repointed between a fresh in-memory object and a query entity; with
        // more than one assignment we cannot prove which object any given read sees.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Order t = new Order();
        Console.WriteLine(t.Customer.Name);
        t = orders[0];
        Console.WriteLine(t.Id);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NameofNavigation_NoDiagnostic()
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
            Console.WriteLine(nameof(o.Customer));
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DeconstructionAssignmentToNav_NoDiagnostic()
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
            (o.Customer, o.Status) = (new Customer(), ""done"");
        }
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_IndexedEntityPassedAsArgument_NoDiagnostic()
    {
        // orders[0] escapes to a helper that could explicitly load the navigation.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders[0]);
        Console.WriteLine(orders[0].Customer.Name);
    }

    void Hydrate(Order order) { }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ConditionalCollectionMutatorCall_NoDiagnostic()
    {
        // order?.Items?.Add(x) is the null-guarded spelling of the mutation pattern that the
        // rule deliberately ignores: the Add call hangs off a conditional access, so the
        // mutator-receiver check must look through the placeholder.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order?.Items?.Add(new OrderItem());
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ChainedConditionalAccessCoveredByInclude_NoDiagnostic()
    {
        // order?.Customer?.Name nests two conditional accesses — the shape that previously
        // sent TryGetAccessPath into infinite recursion. With the Include present the
        // analyzer must both terminate and stay quiet.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        Console.WriteLine(order?.Customer?.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AggregateTerminal_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var count = db.Orders.Count();
        Console.WriteLine(count);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
