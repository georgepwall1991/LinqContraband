namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Indexing into a navigation collection yields one of the instances it holds, exactly like the
/// extractors. `order.Items[0]` is the idiomatic spelling of `order.Items.ElementAt(0)`.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_NavigationCollectionIndexerInline_ReportsNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Items[0].Product|}.Name);
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_NavigationCollectionIndexerIntoLocal_ReportsNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = order.Items[0];
            Console.WriteLine({|#0:item.Product|}.Name);
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionIndexerOnIncludedPath_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            var item = order.Items[0];
            Console.WriteLine(item.Product.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionIndexerAfterParentEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            Handle(order);
            var item = order.Items[0];
            Console.WriteLine(item.Product.Name);
        }
    }

    void Handle(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_IndexerOnAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<OrderItem> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = other[0];
            Console.WriteLine(item.Product.Name);
        }
    }
"
        );
    }
}
