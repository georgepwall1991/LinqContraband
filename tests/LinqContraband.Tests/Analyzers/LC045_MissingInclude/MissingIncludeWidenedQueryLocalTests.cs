using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Widening a query's static type to <c>IEnumerable&lt;T&gt;</c> changes nothing about what EF
/// does: `foreach (var o in source)` still runs the query. The same loop over `source.ToList()`
/// was already reported, so leaving the direct form quiet was an inconsistency rather than a
/// deliberate limit. The chain proof still has to reach a DbSet root, and the local must be
/// assigned the query exactly once before the loop.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("db.Orders")]
    [InlineData("db.Orders.Where(o => o.Id > 0)")]
    [InlineData("db.Orders.OrderBy(o => o.Id)")]
    [InlineData("db.Orders.AsNoTracking()")]
    public async Task TestCrime_ForeachOverAWidenedQueryLocal_Reports(string assigned)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = "
                + assigned
                + @";
        foreach (var order in source)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ForeachOverAWidenedQueryLocal_ReportsOnlyTheMissingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders.Include(o => o.Items);
        foreach (var order in source)
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
    public async Task TestInnocent_ForeachOverAWidenedIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders.Include(o => o.Customer);
        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverAReassignedWidenedLocal_StaysQuiet()
    {
        // The loop need not enumerate what the declaration assigned, so the query behind the
        // name is no longer known.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders;
        source = new List<Order>();
        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverAConditionallyBoundWidenedLocal_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag)
    {
        var db = new MyDbContext();
        IEnumerable<Order> source;
        if (flag)
            source = db.Orders;
        else
            source = new List<Order>();

        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverAWidenedPlainList_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = new List<Order>();
        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverAWidenedNonEfQuery_StaysQuiet()
    {
        // A LINQ-to-objects query is queryable, but the chain proof never reaches a DbSet root.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var list = new List<Order>();
        IEnumerable<Order> source = list.AsQueryable().Where(o => o.Id > 0);
        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ForeachOverAWidenedLocalAssignedAfterTheLoop_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = new List<Order>();
        foreach (var order in source)
        {
            Console.WriteLine(order.Customer.Name);
        }

        source = db.Orders;
    }
"
        );
    }
}
