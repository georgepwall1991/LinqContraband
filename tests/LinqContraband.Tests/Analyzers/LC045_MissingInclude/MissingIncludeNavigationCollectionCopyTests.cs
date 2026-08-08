namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Copying a child collection before using it — `order.Items.ToList()` — is the ordinary way to
/// avoid mutating during enumeration. The copy is a different collection holding the very same
/// entity instances, so every nested read through it is the same read.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("order.Items.ToList()")]
    [InlineData("order.Items.ToArray()")]
    [InlineData("order.Items.Where(i => i.Id > 0).ToList()")]
    public async Task TestCrime_ForeachOverANavigationCollectionCopy_ReportsNestedPath(string copy)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            foreach (var item in "
                + copy
                + @")
            {
                Console.WriteLine({|#0:item.Product|}.Name);
            }
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Theory]
    [InlineData("order.Items.ToList()[0]")]
    [InlineData("order.Items.ToList().First()")]
    [InlineData("order.Items.Where(i => i.Id > 0).ToList()[0]")]
    public async Task TestCrime_ElementFromANavigationCollectionCopy_ReportsNestedPath(
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
    public async Task TestCrime_AggregateOverANavigationCollectionCopy_ReportsNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            var total = order.Items.ToList().Sum(i => {|#0:i.Product|}.Id);
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionCopyOnIncludedPath_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items.ToList())
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_QueryMaterializerIsNotACollectionCopy_KeepsReportingOnce()
    {
        // db.Orders.ToList() is the query materializer, not a copy of a navigation collection.
        // Its receiver is a DbSet, so the copy proof cannot see it and the diagnostic is
        // unchanged: one finding, at the first read.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
