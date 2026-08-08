using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// An in-memory view over a materialized collection — `orders.Where(...)`, `orders.OrderBy(...)`,
/// `orders.Take(n)` — yields the same entity instances the query produced. Iterating the view is
/// therefore the same read the loop over the collection itself performs.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("orders.Where(o => o.Id > 0)")]
    [InlineData("orders.OrderBy(o => o.Id)")]
    [InlineData("orders.OrderByDescending(o => o.Id)")]
    [InlineData("orders.Skip(1)")]
    [InlineData("orders.Take(10)")]
    [InlineData("orders.Distinct()")]
    [InlineData("orders.AsEnumerable().Reverse()")]
    [InlineData("orders.AsEnumerable()")]
    [InlineData("orders.OrderBy(o => o.Id).ThenBy(o => o.Status)")]
    [InlineData("orders.Where(o => o.Id > 0).OrderBy(o => o.Status).Take(5)")]
    public async Task TestCrime_ForeachOverMaterializedCollectionView_Reports(string view)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in "
                + view
                + @")
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ForeachOverViewOfIncludedQuery_ReportsOnlyTheMissingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders.Where(o => o.Items.Count > 0))
        {
            Console.WriteLine(order.Items.Count);
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverViewWithEffectfulPredicate_StaysQuiet()
    {
        // The predicate can hand the entity to a helper that explicitly loads the navigation,
        // so the view is no longer a proof-preserving projection of the materialized collection.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.Where(o => Load(o)))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    bool Load(Order order) => true;
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverProjectionView_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.Select(o => Rewrap(o)))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    Order Rewrap(Order order) => order;
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverCustomExtensionView_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in Filter(orders))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    IEnumerable<Order> Filter(List<Order> orders) => orders;
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverViewAfterResultEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Handle(orders);
        foreach (var order in orders.Where(o => o.Id > 0))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Handle(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverViewOfUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in other.Where(o => o.Id > 0))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }
}
