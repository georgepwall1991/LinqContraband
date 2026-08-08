using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Naming the materialized collection — `var active = orders.Where(...);` — is the ordinary way
/// to write a read that is already reported inline. The local stands in for the collection, so
/// every consumer reads through it, and handing it out escapes the collection itself. A
/// reassigned, conditionally bound, or unrelated local is not that collection.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("orders")]
    [InlineData("orders.Where(o => o.Id > 0)")]
    [InlineData("orders.Where(o => o.Id > 0).ToList()")]
    [InlineData("orders.ToList()")]
    [InlineData("orders.OrderBy(o => o.Id).Take(5)")]
    [InlineData("orders.AsEnumerable()")]
    [InlineData("from o in orders where o.Id > 0 select o")]
    public async Task TestCrime_ForeachOverACollectionAliasLocal_Reports(string bound)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = "
                + bound
                + @";
        foreach (var order in alias)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ExtractionFromACollectionAliasLocal_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        var one = alias.First();
        Console.WriteLine({|#0:one.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_CallbackOverACollectionAliasLocal_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        Console.WriteLine(alias.Sum(o => {|#0:o.Customer|}.Rating));
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AliasOfAnAlias_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders.Where(o => o.Id > 0);
        var second = first.Take(5);
        foreach (var order in second)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AliasOfAnIncludedQuery_ReportsOnlyTheMissingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        var alias = orders.Where(o => o.Items.Count > 0).ToList();
        foreach (var order in alias)
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
    public async Task TestInnocent_AliasOfAnIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AliasEscapesBeforeTheRead_StaysQuiet()
    {
        // The alias holds the very instances the collection holds, so a helper given the alias
        // could have loaded the navigation on them. Escaping the name escapes the collection.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        Hydrate(alias);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ReadThroughAnEscapedAlias_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        Hydrate(alias);
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestCrime_UnrelatedCollectionEscape_DoesNotSilenceTheRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var unrelated = new List<Order>();
        Hydrate(unrelated);
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ScalarProjectionEscape_DoesNotSilenceTheRead()
    {
        // `orders.Select(o => o.Status)` hands out strings, not entities, so nothing that
        // escapes there could have loaded a navigation.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(o => o.Status).ToList();
        Consume(names);
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }

    void Consume(List<string> names) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_ReassignedAliasLocal_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => o.Id > 0).ToList();
        alias = new List<Order>();
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ConditionallyBoundAliasLocal_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool flag)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        List<Order> alias;
        if (flag)
            alias = orders.ToList();
        else
            alias = new List<Order>();

        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AliasOfAnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = new List<Order>();
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AliasBuiltByAProjection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Select(o => Rewrap(o)).ToList();
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    static Order Rewrap(Order order) => order;
"
        );
    }

    [Fact]
    public async Task TestInnocent_AliasBuiltByAnEffectfulView_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var alias = orders.Where(o => Load(o)).ToList();
        foreach (var order in alias)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    bool Load(Order order) => true;
"
        );
    }
}
