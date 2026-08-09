using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// KNOWN GAP, pinned so closing it is deliberate and so it cannot widen unnoticed.
///
/// A navigation read on a loop variable inside an <b>expression-level conditional</b> — a ternary,
/// a switch expression, <c>?.</c> on the navigation itself, or <c>??</c> — is not reported, while
/// the equivalent <c>if</c>/<c>else</c> statement is. It is not branch conservatism: the ternary
/// stays quiet even when <b>both arms</b> read the navigation, so there is no path on which the
/// read does not happen. The same read on a single materialized entity is reported, so the gap is
/// specific to the loop-variable flow proof.
///
/// The fix belongs in the origin-flow prover's event-to-block mapping rather than in read
/// collection, which is why it is recorded here rather than patched at the edges.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_ReadInsideAnIfElseOnTheLoopVariable_Reports()
    {
        // The control case: the statement form works, which is what makes the expression form a
        // defect rather than a decision.
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
    [InlineData(@"var name = order.Id > 0 ? order.Customer.Name : """";")]
    [InlineData(@"var name = order.Id > 0 ? order.Customer.Name : order.Customer.Name;")]
    [InlineData(@"var name = order.Id switch { 1 => order.Customer.Name, _ => """" };")]
    [InlineData(@"var name = order.Customer?.Name;")]
    [InlineData(@"var name = order.Customer?.Name ?? """";")]
    [InlineData(@"var customer = order.Customer ?? new Customer();")]
    public async Task TestKnownGap_ReadInsideAnExpressionConditionalOnTheLoopVariable_StaysQuiet(
        string read
    )
    {
        // Every one of these should report. The second reads the navigation in BOTH arms, so no
        // path avoids it — the silence is a defect in the flow proof, not conservatism.
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
"
        );
    }
}
