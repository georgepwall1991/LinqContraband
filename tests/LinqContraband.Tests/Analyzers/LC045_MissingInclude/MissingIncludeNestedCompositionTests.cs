namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The nested-navigation surface was built across 5.7.18 through 5.7.21 — views, callbacks,
/// element extraction and indexing — each in its own pass. These pin how they compose, and the
/// boundaries they share, so that extending any one of them cannot quietly move the others.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_NestedSurfacesCompose_AcrossViewsCallbacksAndExtraction()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();

        // The parent entity itself came from an extraction, not a loop.
        var first = orders.First();
        Console.WriteLine(first.Items.Sum(i => {|#0:i.Product|}.Id));
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_NestedViewsComposeThreeLevelsDeep()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items.Where(i => i.Id > 0))
            {
                foreach (var detail in item.Details.Take(2))
                {
                    Console.WriteLine({|#0:detail.Product|}.Name);
                }
            }
        }
    }
",
            Diagnostic(0, "Items.Details.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_NestedCallbackParameterMayShadowTheOuterName()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var o in orders)
        {
            var total = o.Items.Sum(o2 => {|#0:o2.Product|}.Id);
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_NestedSurfaceBoundaries_StayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<OrderItem> other, Product product)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            // A write is not a read.
            order.Items[0].Product = product;

            // nameof evaluates nothing.
            Console.WriteLine(nameof(order.Items));

            // An unrelated collection is not this navigation.
            Console.WriteLine(other[0].Product.Name);

            // A scalar callback reads no navigation.
            var ids = order.Items.Sum(i => i.Id);

            // Count is a property of the collection, not a navigation read.
            Console.WriteLine(order.Items.Count);

            // A lambda capturing the entity is an escape, so its reads are not proven.
            Action show = () => Console.WriteLine(order.Items[0].Product.Name);
            show();
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NestedSurfacesOnAnIncludedPath_StayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Items[0].Product.Name);
            var total = order.Items.Sum(i => i.Product.Id);
            var picked = order.Items.First(i => i.Id > 0);
            Console.WriteLine(picked.Product.Name);

            foreach (var item in order.Items.Where(i => i.Id > 0))
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
"
        );
    }
}
