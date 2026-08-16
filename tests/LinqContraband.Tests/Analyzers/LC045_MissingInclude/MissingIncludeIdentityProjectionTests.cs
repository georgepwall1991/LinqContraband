using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// LINQ query syntax always lowers its trailing <c>select x</c> to an identity projection
/// <c>Select(x =&gt; x)</c>. That projection reshapes nothing — it yields the very entity
/// instances the source produced — so it must not hide the missing eager load. Any other
/// selector is a real projection and stays out of scope.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("(from o in db.Orders select o).ToList()")]
    [InlineData("(from o in db.Orders where o.Id > 0 select o).ToList()")]
    [InlineData("(from o in db.Orders orderby o.Id select o).ToList()")]
    [InlineData("(from o in db.Orders where o.Id > 0 orderby o.Status select o).ToList()")]
    [InlineData("db.Orders.Select(o => o).ToList()")]
    [InlineData("db.Orders.Where(o => o.Id > 0).Select(o => o).ToList()")]
    [InlineData("db.Orders.Select(o => o).Where(o => o.Id > 0).ToList()")]
    public async Task TestCrime_IdentityProjectedQuery_Reports(string query)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = "
                + query
                + @";
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
    public async Task TestCrime_IdentityProjectedElementMaterializer_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = (from o in db.Orders where o.Id > 0 select o).First();
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("from x in orders where x.Id > 0 select x")]
    [InlineData("from x in orders orderby x.Id select x")]
    [InlineData("orders.Select(x => x)")]
    [InlineData("orders.Where(x => x.Id > 0).Select(x => x)")]
    public async Task TestCrime_IdentityProjectedInMemoryView_Reports(string view)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in "
                + view
                + @")
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_IdentityProjectedQuery_ReportsOnlyTheMissingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders.Include(o => o.Items) select o).ToList();
        foreach (var order in orders)
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
    public async Task TestInnocent_IdentityProjectedIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders.Include(o => o.Customer) select o).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Theory]
    [InlineData("db.Orders.Select(o => o.Id > 0 ? o : o).ToList()")]
    [InlineData("db.Orders.Select(o => Rewrap(o)).ToList()")]
    [InlineData("db.Orders.Select((o, i) => o).ToList()")]
    [InlineData("db.Orders.Select(Rewrap).ToList()")]
    public async Task TestInnocent_NonIdentityProjection_StaysQuiet(string query)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = "
                + query
                + @";
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    static Order Rewrap(Order order) => order;
"
        );
    }

    [Fact]
    public async Task TestInnocent_InMemoryNonIdentityProjection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders.Select(o => Rewrap(o)))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    static Order Rewrap(Order order) => order;
"
        );
    }

    [Fact]
    public async Task TestInnocent_UpcastingProjection_StaysQuiet()
    {
        // `Select<SpecialOrder, OrderBase>(o => o)` hands back the same instances, but the
        // element type it yields is no longer the queried entity, so the shape proof must not
        // treat the body-is-the-parameter test alone as identity.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.SpecialOrders.Select<SpecialOrder, OrderBase>(o => o).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_IdentityProjectedAliasEscapes_StaysQuiet()
    {
        // 5.7.28 reported here, because the alias was not understood to be the collection.
        // Now that it is, handing it to a helper that could load the navigation discards the
        // proof exactly as handing out `orders` itself does.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var aliases = orders.Select(o => o).ToList();
        Hydrate(aliases);
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
    public async Task TestInnocent_IdentityProjectedQueryEscapesBeforeRead_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders select o).ToList();
        Hydrate(orders);
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
    public async Task TestCrime_ReadThroughAnIdentityProjectedAliasLocal_Reports()
    {
        // The residual 5.7.28 recorded here — a view or copy held in its own local not carrying
        // the collection's origin — is closed: the alias now stands in for the collection.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var aliases = orders.Select(o => o).ToList();
        foreach (var order in aliases)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
