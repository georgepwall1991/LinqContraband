namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The surfaces added in 5.7.11 through 5.7.18 were all developed against `List&lt;T&gt;` results.
/// These pin that they follow the materialized collection itself rather than that one type: an
/// array, a set, and interface-typed locals carry the same origin.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("var orders = db.Orders.ToArray();")]
    [InlineData("var orders = db.Orders.ToHashSet();")]
    [InlineData("List<Order> orders = db.Orders.ToList();")]
    [InlineData("IList<Order> orders = db.Orders.ToList();")]
    [InlineData("IReadOnlyList<Order> orders = db.Orders.ToList();")]
    public async Task TestCrime_MaterializedCollectionShape_DoesNotChangeTheDiagnostic(
        string declaration
    )
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        "
                + declaration
                + @"
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("foreach (var order in orders.Where(o => o.Id > 0)) { Console.WriteLine({|#0:order.Customer|}.Name); }")]
    [InlineData("var count = orders.Count(o => {|#0:o.Customer|}.Name != null);")]
    [InlineData("var order = orders.First(o => o.Id == 1); Console.WriteLine({|#0:order.Customer|}.Name);")]
    [InlineData("var map = orders.ToDictionary(o => {|#0:o.Customer|}.Name);")]
    public async Task TestCrime_NewSurfacesWorkOverAnArrayResult(string statement)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToArray();
        "
                + statement
                + @"
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
