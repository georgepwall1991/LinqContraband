namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// `orders.ToList()` copies the materialized collection but holds the very same entity
/// instances, so every read through the copy is the same read. The query materializer is itself
/// a `ToList`, so the copy proof is deliberately gated on the source already being proven.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("orders.ToList()")]
    [InlineData("orders.ToArray()")]
    [InlineData("orders.Where(o => o.Id > 0).ToList()")]
    public async Task TestCrime_ForeachOverAMaterializedCollectionCopy_Reports(string copy)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in "
                + copy
                + @")
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("orders.ToList().First()")]
    [InlineData("orders.ToList().Last()")]
    [InlineData("orders.ToList()[0]")]
    [InlineData("orders.ToArray()[0]")]
    [InlineData("orders.Where(o => o.Id > 0).ToList()[0]")]
    public async Task TestCrime_ElementFromAMaterializedCollectionCopy_Reports(string extraction)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = "
                + extraction
                + @";
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AggregateOverAMaterializedCollectionCopy_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var total = orders.ToList().Sum(o => {|#0:o.Customer|}.Id);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_MaterializedCollectionCopyOfAnIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var order in orders.ToList())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CopyOfAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in other.ToList())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CopyAfterTheCollectionEscapes_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Handle(orders);
        foreach (var order in orders.ToList())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Handle(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerIsNotACopy_StillReportsOnce()
    {
        // db.Orders.ToList() is the materializer. It must not be treated as a copy of itself,
        // and the finding stays exactly one diagnostic at the first read.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        foreach (var order in db.Orders.ToList())
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_IndexerIntoACopyOfAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = other.ToList()[0];
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }
}
