using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// An ordering key selector reads the entity exactly like a `Where` predicate does. Sorting a
/// materialized list by a navigation is a per-element read of a navigation the query never loaded.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("OrderBy")]
    [InlineData("OrderByDescending")]
    public async Task TestCrime_OrderingKeySelectorReadsMissingNavigation_Reports(string ordering)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders."
                + ordering
                + @"(o => {|#0:o.Customer|}.Name))
        {
            Console.WriteLine(order.Id);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("ThenBy")]
    [InlineData("ThenByDescending")]
    public async Task TestCrime_SecondaryOrderingKeySelectorReadsMissingNavigation_Reports(
        string ordering
    )
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.OrderBy(o => o.Id)."
                + ordering
                + @"(o => {|#0:o.Customer|}.Name))
        {
            Console.WriteLine(order.Id);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OrderingKeySelectorNestedPath_ReportsFullPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var order in orders.OrderBy(o => {|#0:o.Customer.Address|}.City))
        {
            Console.WriteLine(order.Id);
        }
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_OrderingKeySelectorOnIncludedNavigation_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var order in orders.OrderBy(o => o.Customer.Name))
        {
            Console.WriteLine(order.Id);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OrderingKeySelectorOnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in other.OrderBy(o => o.Customer.Name))
        {
            Console.WriteLine(order.Id);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OrderingKeySelectorAfterCollectionEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Handle(orders);
        foreach (var order in orders.OrderBy(o => o.Customer.Name))
        {
            Console.WriteLine(order.Id);
        }
    }

    void Handle(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OrderingKeySelectorScalarOnly_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.OrderBy(o => o.Status))
        {
            Console.WriteLine(order.Id);
        }
    }
"
        );
    }
}
