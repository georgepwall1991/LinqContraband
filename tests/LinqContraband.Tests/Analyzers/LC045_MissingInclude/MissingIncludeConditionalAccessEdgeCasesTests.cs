using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_ConditionalCollectionMutatorCall_NoDiagnostic()
    {
        // order?.Items?.Add(x) is the null-guarded spelling of the mutation pattern that the
        // rule deliberately ignores: the Add call hangs off a conditional access, so the
        // mutator-receiver check must look through the placeholder.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order?.Items?.Add(new OrderItem());
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ChainedConditionalAccessCoveredByInclude_NoDiagnostic()
    {
        // order?.Customer?.Name nests two conditional accesses - the shape that previously
        // sent TryGetAccessPath into infinite recursion. With the Include present the
        // analyzer must both terminate and stay quiet.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        Console.WriteLine(order?.Customer?.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NavAssignedThenChainedConditionalRead_NoDiagnostic()
    {
        // The write backs the later chained-?. read; satisfaction must work through
        // conditional-access placeholders just as it does for plain reads.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order.Customer = new Customer();
        Console.WriteLine(order?.Customer?.Name);
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_IncludeCoversInlineChainedConditionalAccess_NoDiagnostic()
    {
        // The chained-?. inline shape stays quiet when the Include covers the path.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var name = db.Orders.Include(o => o.Customer).FirstOrDefault()?.Customer?.Name;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ParenthesizedConditionalAccessReportsFullNestedPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        Console.WriteLine((order?.Customer)?{|#0:.Address|}?.City);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_DeeperParenthesizedConditionalAccessReportsFullNestedPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(o => o.Customer)
            .ThenInclude(c => c.Address)
            .FirstOrDefault();
        Console.WriteLine((order?.Customer?.Address)?{|#0:.Region|}?.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address.Region", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerParenthesizedConditionalAccessReportsFullNestedPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var city = (db.Orders.Include(o => o.Customer).FirstOrDefault()?.Customer)?{|#0:.Address|}?.City;
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InheritedNavigationParenthesizedConditionalAccessReportsFullNestedPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.SpecialOrders.Include(o => o.Customer).FirstOrDefault();
        Console.WriteLine((order?.Customer)?{|#0:.Address|}?.City);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "SpecialOrder");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_ConditionalMethodReturnDoesNotAppendReceiverPath_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var city = (order?.Customer.GetDetached())?.Address?.City;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
