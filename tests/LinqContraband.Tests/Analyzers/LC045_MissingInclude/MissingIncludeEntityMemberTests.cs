using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A computed property the entity declares itself: <c>public string CustomerLabel =&gt; Customer.Name;</c>.
/// Reading <c>order.CustomerLabel</c> runs that getter, so it reads <c>order.Customer</c> exactly
/// as if the caller had written it. The getter is read only for navigations on <c>this</c>, and
/// only when nothing in it could load them.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_ComputedPropertyThatReadsANavigation_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.CustomerLabel|});
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ComputedPropertyWithAGetBlock_ReportsTheNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.ShippingCity|});
        }
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ComputedPropertyOverACollectionNavigation_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    int Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        return {|#0:order.ItemCount|};
    }
",
            Diagnostic(0, "Items", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ComputedPropertyOnALoadedNavigation_ReportsTheCombinedPath()
    {
        // Customer is included; Customer.CityLabel reads Customer.Address, which is not.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer.CityLabel|});
        }
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Theory]
    [InlineData("var labels = orders.Select(o => {|#0:o.CustomerLabel|}).ToList();")]
    [InlineData("var sorted = orders.OrderBy(o => {|#0:o.CustomerLabel|}).ToList();")]
    [InlineData("orders.ForEach(o => Console.WriteLine({|#0:o.CustomerLabel|}));")]
    [InlineData("var first = orders[0]; Console.WriteLine({|#0:first.CustomerLabel|});")]
    [InlineData("var labels = orders.Select(Label).ToList();")]
    public async Task TestCrime_ComputedPropertyReadThroughOtherShapes_Reports(string statement)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        "
                + statement
                + @"
    }

    private static string Label(Order order) => "
                + (statement.Contains("Select(Label)") ? "{|#0:order.CustomerLabel|}" : "order.Status")
                + @";
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ComputedPropertyThroughConditionalAccess_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    string Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        return order?{|#0:.CustomerLabel|};
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.Include(o => o.Customer).ToList()", "order.CustomerLabel")]
    [InlineData("db.Orders.Include(o => o.Customer.Address).ToList()", "order.ShippingCity")]
    [InlineData("db.Orders.Include(o => o.Customer).ThenInclude(c => c.Address).ToList()", "order.Customer.CityLabel")]
    [InlineData("db.Orders.Include(o => o.Items).ToList()", "order.ItemCount")]
    public async Task TestInnocent_ComputedPropertyOverAnIncludedNavigation_StaysQuiet(string query, string read)
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
            Console.WriteLine("
                + read
                + @");
        }
    }
"
        );
    }

    [Theory]
    // Reads no navigation.
    [InlineData("order.StatusLabel")]
    [InlineData("order.NavigationName")]
    [InlineData("Order.SharedLabel")]
    // An overridable property may run a different body.
    [InlineData("order.OverridableLabel")]
    // Anything the entity calls on itself, or hands itself to, could load the navigation.
    [InlineData("order.SelfLoadingLabel")]
    [InlineData("order.HandsItselfOutLabel")]
    // The getter assigns the navigation before it reads it.
    [InlineData("order.DefaultingLabel")]
    public async Task TestDeliberate_ComputedPropertyThatIsNotAProvenRead_MustStayQuiet(string read)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine("
                + read
                + @");
        }
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_ComputedPropertyAfterTheNavigationIsAssigned_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        order.Customer = new Customer();
        Console.WriteLine(order.CustomerLabel);
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_ComputedPropertyAfterAnExplicitLoad_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        db.Entry(order).Reference(o => o.Customer).Load();
        Console.WriteLine(order.CustomerLabel);
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_ComputedPropertyAfterTheEntityEscapes_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        Hydrate(order);
        Console.WriteLine(order.CustomerLabel);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ComputedPropertyOnAnEntityThatWasNotQueried_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var order = new Order { Customer = new Customer() };
        Console.WriteLine(order.CustomerLabel);
    }
"
        );
    }
}
