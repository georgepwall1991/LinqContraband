namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Manual relationship fix-up: a loop over the whole materialized collection that writes a
/// navigation on every element leaves no element unwritten, so a later read through a different
/// origin is not a missing Include. The write has to be unconditional and over the collection
/// itself — anything that can skip an element puts the read back in doubt.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_FixUpLoopThenSecondLoop_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_FixUpLoopThenAggregateCallback_StillReports()
    {
        // A callback body is analysed in its own control-flow graph, which the collection-level
        // fact does not reach, so this shape is still reported. Recorded in the candidate queue;
        // pinned here so closing it is a deliberate change rather than a surprise.
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        var total = orders.Sum(o => {|#0:o.Customer|}.Id);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_FixUpLoopThenIndexedRead_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        Console.WriteLine(orders[0].Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_ConditionalFixUpWrite_StillReports()
    {
        // A branch can leave an element unwritten, so the later read is not covered.
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer, bool flag)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            if (flag)
            {
                order.Customer = customer;
            }
        }

        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_FixUpLoopOverAFilteredView_StillReports()
    {
        // A filtered loop writes only the elements that passed the filter.
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.Where(o => o.Id > 0))
        {
            order.Customer = customer;
        }

        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_FixUpWritesADifferentNavigation_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Items|}.Count);
        }
    }
",
            Diagnostic(0, "Items", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ReadBeforeTheFixUpLoop_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        foreach (var order in orders)
        {
            order.Customer = customer;
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_FixUpLoopWritingANestedPath_CoversTheNestedRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(Customer customer)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Address.City);
        }
    }
"
        );
    }
}
