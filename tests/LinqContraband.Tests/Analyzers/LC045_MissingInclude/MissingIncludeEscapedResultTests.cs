using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_ResultLocalReassigned_NoDiagnostic()
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
        orders = new List<Order>();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultPassedAsArgument_NoDiagnostic()
    {
        // The helper may explicitly load the navigations; once the list escapes we cannot
        // prove the access is unbacked.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ResultReturned_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class Program
{
    List<Order> Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine(orders.Count);
        return orders;
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ResultUsedThroughExactSelectCallback_Reports()
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
        var names = orders.Select(o => {|#0:o.Customer|}.Name).ToList();
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_ExactListForEachOnResult_Reports()
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
        orders.ForEach(o => Console.WriteLine({|#0:o.Customer|}.Name));
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestInnocent_EntityPassedToEntry_NoDiagnostic()
    {
        // db.Entry(order) may be used to explicitly load the navigation; the entity escaping
        // as an argument keeps the rule quiet.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        db.Entry(order);
        Console.WriteLine(order.Customer.Name);
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityLocalReusedAcrossObjects_NoDiagnostic()
    {
        // The local is repointed between a fresh in-memory object and a query entity; with
        // more than one assignment we cannot prove which object any given read sees.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Order t = new Order();
        Console.WriteLine(t.Customer.Name);
        t = orders[0];
        Console.WriteLine(t.Id);
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_IndexedEntityPassedAsArgument_NoDiagnostic()
    {
        // orders[0] escapes to a helper that could explicitly load the navigation.
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders[0]);
        Console.WriteLine(orders[0].Customer.Name);
    }

    void Hydrate(Order order) { }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
