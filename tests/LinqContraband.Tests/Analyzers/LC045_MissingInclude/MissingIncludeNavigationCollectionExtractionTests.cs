namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Pulling one item out of a navigation collection yields an instance that collection holds, so
/// reading a navigation on it is the same nested read as the `foreach` over `order.Items`.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("order.Items.First()")]
    [InlineData("order.Items.First(i => i.Id > 0)")]
    [InlineData("order.Items.FirstOrDefault(i => i.Id > 0)")]
    [InlineData("order.Items.Single(i => i.Id > 0)")]
    [InlineData("order.Items.Last()")]
    [InlineData("order.Items.ElementAt(0)")]
    [InlineData("order.Items.Where(i => i.Id > 0).First()")]
    [InlineData("order.Items.OrderBy(i => i.Id).First()")]
    public async Task TestCrime_NavigationCollectionElementExtraction_ReportsNestedPath(
        string extraction
    )
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = "
                + extraction
                + @";
            Console.WriteLine({|#0:item.Product|}.Name);
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionExtractionOnIncludedPath_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            var item = order.Items.First(i => i.Id > 0);
            Console.WriteLine(item.Product.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionExtractionAfterParentEscape_StaysQuiet()
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
            var item = order.Items.First(i => i.Id > 0);
            Console.WriteLine(item.Product.Name);
        }
    }

    void Handle(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionExtractionThenEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = order.Items.First(i => i.Id > 0);
            Take(item);
            Console.WriteLine(item.Product.Name);
        }
    }

    void Take(OrderItem item) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionEscapedToAHelper_StaysQuiet()
    {
        // Handing the collection itself to a helper is a real escape: the helper may load the
        // navigation. Only the framework element extractors are exempt.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = Pick(order.Items);
            Console.WriteLine(item.Product.Name);
        }
    }

    OrderItem Pick(List<OrderItem> items) => items[0];
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractionFromAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<OrderItem> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var item = other.First(i => i.Id > 0);
            Console.WriteLine(item.Product.Name);
        }
    }
"
        );
    }
}
