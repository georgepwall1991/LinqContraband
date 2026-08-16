using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Explicit loading is one of EF's own loading mechanisms: after
/// <c>db.Entry(order).Reference(o =&gt; o.Customer).Load()</c> the navigation is populated. It is
/// therefore recorded as the same fact a manual write records, so the fix-up machinery applies
/// unchanged — a load that reaches every element speaks for the collection, a conditional one or
/// one for another navigation does not.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("db.Entry(order).Reference(o => o.Customer).Load();")]
    [InlineData("db.Entry(order).Reference(\"Customer\").Load();")]
    public async Task TestInnocent_ExplicitLoadOverTheWholeCollection_StaysQuiet(string load)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            "
                + load
                + @"
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
    public async Task TestInnocent_ExplicitLoadThenReadThroughAnAlias_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            db.Entry(order).Reference(o => o.Customer).Load();
        }

        var active = orders.Where(o => o.Id > 0);
        foreach (var order in active)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_ExplicitLoadOfADifferentNavigation_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            db.Entry(order).Reference(o => o.BillingCustomer).Load();
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
    public async Task TestCrime_ConditionalExplicitLoad_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            if (order.Id > 0)
                db.Entry(order).Reference(o => o.Customer).Load();
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
    public async Task TestCrime_ConditionalExplicitLoadAsTheLoopBody_StillReports()
    {
        // `foreach (...) if (c) ...;` makes the `if` itself the loop body. Walking up to the loop
        // body would otherwise accept a load only some elements receive.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
            if (order.Id > 0)
                db.Entry(order).Reference(o => o.Customer).Load();

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
    public async Task TestCrime_ConditionalFixUpWriteAsTheLoopBody_StillReports()
    {
        // The same hole for a manual write: this was silently credited to the whole collection.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var customer = new Customer();
        foreach (var order in orders)
            if (order.Id > 0)
                order.Customer = customer;

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
    public async Task TestInnocent_UnconditionalFixUpWriteAsTheLoopBody_StaysQuiet()
    {
        // A bare statement loop body is still straight-line and must keep working.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var customer = new Customer();
        foreach (var order in orders)
            order.Customer = customer;

        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_ExplicitLoadOverAFilteredView_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.Where(o => o.Id > 0))
        {
            db.Entry(order).Reference(o => o.Customer).Load();
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
}
