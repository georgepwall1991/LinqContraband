using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A navigation read on a loop variable inside an expression-level conditional — a ternary, a
/// switch expression, <c>?.</c> on the navigation, or <c>??</c> — is reported, exactly as the
/// equivalent <c>if</c>/<c>else</c> statement and the single-entity form already were.
///
/// 5.7.37 recorded this as a gap. The cause was that the loop variable's binding was attached to
/// the block holding the body's earliest-starting operation, which for `var s = c ? o.Nav : "";`
/// is the merge block — after the branch that reads the navigation. The read was then visited with
/// the origin unbound, and one unbound visit makes the whole access uncertain.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_ReadInsideAnIfElseOnTheLoopVariable_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            string name;
            if (order.Id > 0)
                name = {|#0:order.Customer|}.Name;
            else
                name = """";

            Console.WriteLine(name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ReadOnASingleEntityInsideATernary_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.First();
        var name = order.Id > 0 ? {|#0:order.Customer|}.Name : """";
        Console.WriteLine(name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData(@"var name = order.Id > 0 ? {|#0:order.Customer|}.Name : """";")]
    [InlineData(@"var name = order.Id switch { 1 => {|#0:order.Customer|}.Name, _ => """" };")]
    [InlineData(@"var name = {|#0:order.Customer|}?.Name;")]
    [InlineData(@"var name = {|#0:order.Customer|}?.Name ?? """";")]
    [InlineData(@"var customer = {|#0:order.Customer|} ?? new Customer();")]
    public async Task TestCrime_ReadInsideAnExpressionConditionalOnTheLoopVariable_Reports(
        string read
    )
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
                + read
                + @"
            Console.WriteLine(1);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_ExpressionConditionalOverAnIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var order in orders)
        {
            var name = order.Id > 0 ? order.Customer.Name : """";
            Console.WriteLine(name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExpressionConditionalAfterAnEscape_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        foreach (var order in orders)
        {
            var name = order.Id > 0 ? order.Customer.Name : """";
            Console.WriteLine(name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }
}
