using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("orders.First()")]
    [InlineData("orders.FirstOrDefault()")]
    [InlineData("orders.Single()")]
    [InlineData("orders.SingleOrDefault()")]
    [InlineData("orders.Last()")]
    [InlineData("orders.LastOrDefault()")]
    [InlineData("orders.ElementAt(0)")]
    [InlineData("orders.ElementAtOrDefault(0)")]
    public async Task TestCrime_ExactFrameworkElementExtractionFromMaterializedCollection_Reports(
        string extraction
    )
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = "
                + extraction
                + @";
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_CustomElementLookalikeAndRepositoryQueryParameter_StayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = CustomOperators.First(orders);
        Console.WriteLine(order.Customer.Name);
    }

    void ReadRepository(IQueryable<Order> query)
    {
        foreach (var order in query)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    private static class CustomOperators
    {
        public static Order First(IEnumerable<Order> orders) => null;
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NestedSystemLinqEnumerableLookalike_StaysQuiet()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = System.Linq.Outer.Enumerable.First(orders);
        Console.WriteLine(order.Customer.Name);
    }
}
"
            + MockNamespace
            + @"
namespace System.Linq
{
    public static class Outer
    {
        public static class Enumerable
        {
            public static T First<T>(IEnumerable<T> source) => default;
        }
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
