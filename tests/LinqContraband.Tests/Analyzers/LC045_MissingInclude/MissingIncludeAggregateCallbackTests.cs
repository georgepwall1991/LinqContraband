using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// An aggregate or quantifier callback over a materialized collection reads the entity once per
/// element, exactly like the `Where` predicate LC045 already follows. Summing or counting by a
/// navigation is a per-element lazy load with proxies, and an aggregate over nulls without one.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("var count = orders.Count(o => {|#0:o.Customer|}.Name != null);")]
    [InlineData("var count = orders.LongCount(o => {|#0:o.Customer|}.Name != null);")]
    [InlineData("var total = orders.Sum(o => {|#0:o.Customer|}.Rating);")]
    [InlineData("var lowest = orders.Min(o => {|#0:o.Customer|}.Rating);")]
    [InlineData("var highest = orders.Max(o => {|#0:o.Customer|}.Rating);")]
    [InlineData("var mean = orders.Average(o => {|#0:o.Customer|}.Rating);")]
    public async Task TestCrime_AggregateCallbackReadsMissingNavigation_Reports(string statement)
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
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("SkipWhile")]
    [InlineData("TakeWhile")]
    public async Task TestCrime_PartitioningPredicateReadsMissingNavigation_Reports(string operatorName)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders."
                + operatorName
                + @"(o => {|#0:o.Customer|}.Name == null))
        {
            Console.WriteLine(order.Id);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("SkipWhile")]
    [InlineData("TakeWhile")]
    public async Task TestCrime_ForeachOverPartitioningView_Reports(string operatorName)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders."
                + operatorName
                + @"(o => o.Id > 0))
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_AggregateCallbackNestedPath_ReportsFullPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var count = orders.Count(o => {|#0:o.Customer.Address|}.City != null);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_EntityReturningMaxSelector_ReportsTheCallbackRead()
    {
        // The selector still reads the navigation once per element, which is the defect. The
        // entity it hands back is a projection boundary, so the result's own reads stay quiet
        // rather than being reported a second time against the query.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var best = orders.Max(o => {|#0:o.Customer|});
        Console.WriteLine(best.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_AggregateCallbackWithHelperCall_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var count = orders.Count(o => Load(o));
    }

    bool Load(Order order) => true;
"
        );
    }

    [Fact]
    public async Task TestInnocent_AggregateCallbackOnIncludedNavigation_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var total = orders.Sum(o => o.Customer.Rating);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AggregateCallbackOnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var total = other.Sum(o => o.Customer.Rating);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_AggregateCallbackAfterCollectionEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Handle(orders);
        var total = orders.Sum(o => o.Customer.Rating);
    }

    void Handle(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_MethodGroupCallbackIsNotInline_StaysQuiet()
    {
        // Only an inline lambda can be proved effect-free; a method group could load the
        // navigation itself, so it stays a boundary.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var count = orders.Count(HasCustomer);
    }

    bool HasCustomer(Order order) => order.Customer != null;
"
        );
    }

    [Fact]
    public async Task TestInnocent_ReadAfterEntityReturningMaxSelector_StaysQuiet()
    {
        // The entity-returning selector hands a navigation out of the sequence, so it stays an
        // escape: a different navigation read afterwards is no longer proven.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var best = orders.Max(o => {|#0:o.Customer|});
        foreach (var order in orders)
        {
            Console.WriteLine(order.Items.Count);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ReadAfterScalarAggregate_StillReports()
    {
        // A scalar aggregate lets nothing escape, so a later read of a different navigation is
        // still proven.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var total = orders.Sum(o => {|#0:o.Customer|}.Rating);
        foreach (var order in orders)
        {
            Console.WriteLine({|#1:order.Items|}.Count);
        }
    }
",
            Diagnostic(0, "Customer", "Order"),
            Diagnostic(1, "Items", "Order")
        );
    }
}
