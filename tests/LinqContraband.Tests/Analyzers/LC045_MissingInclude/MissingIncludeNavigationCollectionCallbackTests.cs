namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A callback over a navigation collection reads each item once, exactly like the `foreach` over
/// `order.Items` that LC045 already follows. `order.Items.Sum(i => i.Product.Price)` is an N+1
/// per item, hidden inside an aggregate.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("var total = order.Items.Sum(i => {|#0:i.Product|}.Id);")]
    [InlineData("var count = order.Items.Count(i => {|#0:i.Product|}.Name != null);")]
    [InlineData("var any = order.Items.Any(i => {|#0:i.Product|}.Name != null);")]
    [InlineData("var all = order.Items.All(i => {|#0:i.Product|}.Name != null);")]
    [InlineData("var best = order.Items.Max(i => {|#0:i.Product|}.Id);")]
    [InlineData("var keyed = order.Items.ToDictionary(i => {|#0:i.Product|}.Name);")]
    [InlineData("var ordered = order.Items.OrderBy(i => {|#0:i.Product|}.Name);")]
    [InlineData("var filtered = order.Items.Where(i => {|#0:i.Product|}.Name != null);")]
    [InlineData("var picked = order.Items.First(i => {|#0:i.Product|}.Name != null);")]
    public async Task TestCrime_NavigationCollectionCallbackReadsMissingNavigation_Reports(
        string statement
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
            "
                + statement
                + @"
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionCallbackOnIncludedPath_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            var total = order.Items.Sum(i => i.Product.Id);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionCallbackAfterParentEscape_StaysQuiet()
    {
        // The helper may have loaded the navigation itself, so the parent entity is no longer a
        // proven origin and its nested reads must go quiet with it.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            Handle(order);
            var total = order.Items.Sum(i => i.Product.Id);
        }
    }

    void Handle(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionCallbackWithHelperCall_StaysQuiet()
    {
        // The callback both hands the item to a helper and reads the navigation. The helper may
        // have loaded it, so the read is not proven — this is what the effect-free requirement
        // buys, and it only bites when a read is actually present.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var total = order.Items.Sum(i => Weigh(i) + i.Product.Id);
        }
    }

    int Weigh(OrderItem item) => 1;
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionCallbackScalarRead_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var count = order.Items.Count(i => i.Sku != null);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CallbackOverAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<OrderItem> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var total = other.Sum(i => i.Product.Id);
        }
    }
"
        );
    }
}
