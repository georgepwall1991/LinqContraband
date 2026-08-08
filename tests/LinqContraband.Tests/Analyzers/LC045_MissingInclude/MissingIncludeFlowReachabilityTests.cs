using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The origin-flow probe only walks blocks from which the analysed access is reachable. That
/// pruning is sound only while the reverse edges it uses are exactly the inverse of the edges the
/// walk follows, so these pin accesses reached through a back-edge, through a conditional
/// successor only, and after a merge.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_AccessReachedOnlyThroughLoopBackEdge_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(int count)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = true;
        for (var i = 0; i < count; i++)
        {
            if (first)
            {
                first = false;
                continue;
            }

            Console.WriteLine({|#0:orders[0].Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AccessReachedOnlyThroughConditionalSuccessor_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag)
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        if (flag)
        {
            Console.WriteLine(order.Id);
        }
        else
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AccessAfterBranchMerge_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag)
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        if (flag)
        {
            Console.WriteLine(order.Id);
        }
        else
        {
            Console.WriteLine(order.Status);
        }

        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_EscapeOnOneBranchBeforeMergedAccess_StaysQuiet()
    {
        // The escape sits on a path that reaches the access only through the merge, so pruning
        // must not drop that predecessor.
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag)
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        if (flag)
        {
            Handle(order);
        }

        Console.WriteLine(order.Customer.Name);
    }

    void Handle(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationWriteOnEveryPathBeforeMergedAccess_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag, Customer customer)
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        if (flag)
        {
            order.Customer = customer;
        }
        else
        {
            order.Customer = customer;
        }

        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }
}
