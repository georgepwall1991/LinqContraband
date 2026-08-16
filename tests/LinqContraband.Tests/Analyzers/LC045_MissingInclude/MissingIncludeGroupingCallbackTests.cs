using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// `ToDictionary`, `ToLookup`, `GroupBy`, `SelectMany` and `DistinctBy` run their callback once
/// per element like every other LINQ operator, but unlike the aggregates their result carries the
/// entities onward. The read inside the callback is reported; the result stays an escape.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("var map = orders.ToDictionary(o => {|#0:o.Customer|}.Name);")]
    [InlineData("var lookup = orders.ToLookup(o => {|#0:o.Customer|}.Name);")]
    [InlineData("var groups = orders.GroupBy(o => {|#0:o.Customer|}.Name);")]
    [InlineData("var items = orders.SelectMany(o => {|#0:o.Customer|}.Name);")]
    public async Task TestCrime_GroupingCallbackReadsMissingNavigation_Reports(string statement)
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

    [Fact]
    public async Task TestCrime_GroupingCallbackNestedPath_ReportsFullPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var map = orders.ToDictionary(o => {|#0:o.Customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_ReadAfterGroupingOperator_StaysQuiet()
    {
        // The dictionary carries the entities onward, so a later read of a different navigation
        // is no longer proven: whoever holds the dictionary may have loaded it.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var map = orders.ToDictionary(o => {|#0:o.Customer|}.Name);
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
    public async Task TestCrime_GroupingCallbackWithHelperCall_Reports()
    {
        // Until 5.7.50 the helper call was an escape, which made this quiet. The helper only reads
        // a navigation on the entity it is handed, so it cannot be the loading mechanism the read
        // needs — and the read is a genuine missing Include.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var map = orders.ToDictionary(o => Key(o));
    }

    string Key(Order order) => {|#0:order.Customer|}.Name;
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_GroupingCallbackOnIncludedNavigation_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var map = orders.ToDictionary(o => o.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_GroupingCallbackOnUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var map = other.ToDictionary(o => o.Customer.Name);
    }
"
        );
    }
}
