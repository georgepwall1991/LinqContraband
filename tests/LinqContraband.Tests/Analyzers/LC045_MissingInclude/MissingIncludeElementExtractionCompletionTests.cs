using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Pulling one element out of a materialized collection yields an instance the query produced,
/// whether the extractor is filtered by a predicate, chosen by a key, or applied to an
/// element-preserving view. Only the bare no-argument overloads were followed before.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("orders.First(x => x.Id == 1)")]
    [InlineData("orders.FirstOrDefault(x => x.Id == 1)")]
    [InlineData("orders.Single(x => x.Id == 1)")]
    [InlineData("orders.SingleOrDefault(x => x.Id == 1)")]
    [InlineData("orders.Last(x => x.Id == 1)")]
    [InlineData("orders.LastOrDefault(x => x.Id == 1)")]
    public async Task TestCrime_PredicateOrKeyElementExtraction_Reports(string extraction)
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

    [Theory]
    [InlineData("orders.Where(x => x.Id > 0).First()")]
    [InlineData("orders.OrderBy(x => x.Id).First()")]
    [InlineData("orders.Skip(1).Take(2).Last()")]
    [InlineData("orders.Distinct().ElementAt(0)")]
    public async Task TestCrime_ElementExtractionFromView_Reports(string extraction)
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
    public async Task TestCrime_ExtractionPredicateReadsMissingNavigation_Reports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders.First(x => {|#0:x.Customer|}.Name == ""a"");
        Console.WriteLine(order.Id);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ExtractedEntityNestedPath_ReportsFullPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var order = orders.First(x => x.Id == 1);
        Console.WriteLine({|#0:order.Customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    // MinBy/MaxBy and the default-value FirstOrDefault overload are .NET 6+, which the suite's
    // default reference set predates.
    private static async Task VerifyOnNet80Async(string programBody, params DiagnosticResult[] expected)
    {
        var test =
            Usings
            + @"
class Program
{
"
            + programBody
            + @"
}
"
            + MockNamespace;

        var analyzerTest = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
        >
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        analyzerTest.ExpectedDiagnostics.AddRange(expected);
        await analyzerTest.RunAsync();
    }

    [Theory]
    [InlineData("MinBy")]
    [InlineData("MaxBy")]
    public async Task TestCrime_KeyBasedElementExtraction_Reports(string extractor)
    {
        await VerifyOnNet80Async(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders."
                + extractor
                + @"(x => x.Id);
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractionWithDefaultValueOverload_StaysQuiet()
    {
        // The default can be an entity that never came from the query, so the extracted local
        // is not provably one of the materialized instances.
        await VerifyOnNet80Async(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders.FirstOrDefault(x => x.Id == 1, new Order());
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractionWithEffectfulPredicate_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders.First(x => Load(x));
        Console.WriteLine(order.Customer.Name);
    }

    bool Load(Order order) => true;
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractionFromUnrelatedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(List<Order> other)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = other.First(x => x.Id == 1);
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractionFromIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var order = orders.First(x => x.Id == 1);
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_ExtractedEntityEscapesBeforeRead_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders.First(x => x.Id == 1);
        Handle(order);
        Console.WriteLine(order.Customer.Name);
    }

    void Handle(Order order) { }
"
        );
    }
}
