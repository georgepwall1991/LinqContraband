using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A mapper handed to a per-element operator as a method group: <c>orders.Select(ToDto)</c>. The
/// method runs once per element exactly as <c>orders.Select(o =&gt; ...)</c> runs its lambda, so
/// its body is analysed as that lambda's body would be. Only a body that is certain to be the one
/// that runs qualifies: a local function of the same method, or a private method in the same file.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("var names = orders.Select(Label).ToList();")]
    [InlineData("var names = orders.Select(new Func<Order, string>(Label)).ToList();")]
    [InlineData("var names = orders.Select(this.Label).ToList();")]
    [InlineData("var first = orders.OrderBy(Label).First();")]
    [InlineData("var lookup = orders.ToLookup(Label);")]
    public async Task TestCrime_PrivateMethodGroupOverTheCollection_Reports(string statement)
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

    private string Label(Order order) => {|#0:order.Customer|}.Name;
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("var vip = orders.Where(IsVip).ToList();")]
    [InlineData("var any = orders.Any(IsVip);")]
    [InlineData("var count = orders.Count(IsVip);")]
    public async Task TestCrime_PrivateStaticPredicateGroupOverTheCollection_Reports(string statement)
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

    private static bool IsVip(Order order)
    {
        return {|#0:order.Customer|}.Rating > 3;
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ListForEachWithPrivateMethodGroup_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders.ForEach(Print);
    }

    private static void Print(Order order) => Console.WriteLine({|#0:order.Customer|}.Name);
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_LocalFunctionGroupOverTheCollection_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        string Label(Order order) => {|#0:order.Customer|}.Name;

        var names = orders.Select(Label).ToList();
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_MethodGroupOverTheInlineMaterializedQuery_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    List<string> Main()
    {
        var db = new MyDbContext();
        return db.Orders.ToList().Select(Label).ToList();
    }

    private static string Label(Order order) => {|#0:order.Customer|}.Name;
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_MethodGroupBodyWithNestedPathAndLocal_ReportsTheLongestPath()
    {
        // The body is analysed as a lambda body would be, so a navigation held in a local keeps
        // its path.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var cities = orders.Select(City).ToList();
    }

    private static string City(Order order)
    {
        var customer = order.Customer;
        return {|#0:customer.Address|}.City;
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_MethodGroupBodyWithANestedCollectionCallback_ReportsTheNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var totals = orders.Select(ProductTotal).ToList();
    }

    private static int ProductTotal(Order order) => order.Items.Sum(item => {|#0:item.Product|}.Id);
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_MethodGroupWithPartOfThePathIncluded_ReportsTheRest()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var cities = orders.Select(City).ToList();
    }

    private static string City(Order order) => {|#0:order.Customer.Address|}.City;
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.Include(o => o.Customer).ToList()")]
    [InlineData("db.Orders.Include(o => o.Customer.Address).ToList()")]
    public async Task TestInnocent_MethodGroupOverAnIncludedQuery_StaysQuiet(string query)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = "
                + query
                + @";
        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order) => order.Customer.Name;
"
        );
    }

    [Fact]
    public async Task TestInnocent_MethodGroupThatReadsOnlyScalars_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var labels = orders.Select(Label).ToList();
    }

    private static string Label(Order order) => order.Id + "" "" + order.Status;
"
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupThatExplicitlyLoads_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    private readonly MyDbContext _db = new MyDbContext();

    void Main()
    {
        var orders = _db.Orders.ToList();
        var names = orders.Select(Label).ToList();
    }

    private string Label(Order order)
    {
        _db.Entry(order).Reference(o => o.Customer).Load();
        return order.Customer.Name;
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_LocalFunctionGroupThatExplicitlyLoads_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        string Label(Order order)
        {
            db.Entry(order).Reference(o => o.Customer).Load();
            return order.Customer.Name;
        }

        var names = orders.Select(Label).ToList();
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_MapperSharedByAnIncludedAndABareQuery_ReportsForTheBareOne()
    {
        await VerifyOriginFlowAsync(
            @"
    List<string> Included()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        return orders.Select(Label).ToList();
    }

    List<string> Bare()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        return orders.Select(Label).ToList();
    }

    private static string Label(Order order) => {|#0:order.Customer|}.Name;
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupThatPassesTheEntityOn_MustStayQuiet()
    {
        // Whatever receives the entity could load the navigation.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(Label).ToList();
    }

    private string Label(Order order)
    {
        Hydrate(order);
        return order.Customer.Name;
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupThatWritesTheNavigationFirst_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order)
    {
        order.Customer = new Customer();
        return order.Customer.Name;
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupAfterAFixUpLoop_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var customer = new Customer();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order) => order.Customer.Name;
"
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupAfterTheCollectionEscapes_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order) => order.Customer.Name;

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_PublicMethodGroup_MustStayQuiet()
    {
        // The same boundary the entity-taking callee has: only a private method or a local
        // function, whose body is in this file and cannot be replaced.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(Label).ToList();
    }

    public virtual string Label(Order order) => order.Customer.Name;
"
        );
    }

    [Fact]
    public async Task TestDeliberate_MethodGroupOnAnotherType_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(Labels.Label).ToList();
    }

    static class Labels
    {
        public static string Label(Order order) => order.Customer.Name;
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_IndexedMethodGroup_StaysQuiet()
    {
        // The two-parameter Select overload is not a supported callback in lambda form either.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order, int index) => index + order.Customer.Name;
"
        );
    }

    [Fact]
    public async Task TestInnocent_MethodGroupOverAnInMemoryList_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var orders = new List<Order> { new Order { Customer = new Customer() } };
        var names = orders.Select(Label).ToList();
    }

    private static string Label(Order order) => order.Customer.Name;
"
        );
    }
}
