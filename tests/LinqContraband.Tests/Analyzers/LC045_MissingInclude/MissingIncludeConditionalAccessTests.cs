using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeTests
{
    [Fact]
    public async Task TestCrime_ConditionalAccessNav_TriggersDiagnostic()
    {
        // order?.Customer is the idiomatic null guard — same deliberate decision as the
        // explicit `!= null` check: the guard does not make the missing Include safe.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Console.WriteLine(order?{|#0:.Customer|}.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ChainedConditionalAccessNav_TriggersDiagnostic()
    {
        // order?.Customer?.Name nests two conditional accesses; Customer's placeholder belongs
        // to the OUTER one. Resolving it to the inner one returns the Customer reference
        // itself, and TryGetAccessPath recurses on its own input until the stack overflows —
        // an uncatchable crash that kills the whole csc process (5.6.0 regression).
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Console.WriteLine(order?{|#0:.Customer|}?.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ChainedConditionalAccessNestedNav_FlagsTheFullPath()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Console.WriteLine(order?.Customer?{|#0:.Address|}?.City);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_MixedPlainAndConditionalChain_FlagsTheFullPath()
    {
        // order?.Customer.Address?.City was a second, independent crash shape: Customer's
        // placeholder walk used to resolve to its own PARENT (the Address reference),
        // producing mutual recursion instead of self-recursion.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Console.WriteLine(order?{|#0:.Customer.Address|}?.City);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineConditionalAccessOnMaterializer_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var name = db.Orders.FirstOrDefault()?{|#0:.Customer|}.Name;
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerChainedConditionalAccess_TriggersDiagnostic()
    {
        // FirstOrDefault()?.Customer?.Name — the chained-?. spelling of the inline shape
        // above. The nav chain entry sits inside a NESTED conditional access, which the
        // entry-property descent must look through.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var name = db.Orders.FirstOrDefault()?{|#0:.Customer|}?.Name;
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerMixedConditionalChain_FlagsTheFullPath()
    {
        // FirstOrDefault()?.Customer.Address?.City — a plain segment between two conditional
        // accesses; the full Customer.Address path must be reported, not just Customer.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var city = db.Orders.FirstOrDefault()?{|#0:.Customer.Address|}?.City;
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer.Address", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_InlineMaterializerConditionalNavMethodCall_TriggersDiagnostic()
    {
        // FirstOrDefault()?.Customer.Clear() — the WhenNotNull arm is an invocation, not a
        // property reference; the entry-property descent must look through the call's
        // instance chain to reach the navigation.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        db.Orders.FirstOrDefault()?{|#0:.Customer|}.Clear();
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ConditionalIndexedAccessOnResult_TriggersDiagnostic()
    {
        // orders?[0].Customer — the null-guarded spelling of the direct indexed access above;
        // the indexer's instance is a conditional-access placeholder that must resolve back
        // to the result local.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine(orders?{|#0:[0].Customer|}.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_LocalFromConditionalIndexer_TriggersDiagnostic()
    {
        // var o = orders?[0] — the initializer is a whole conditional access wrapping the
        // indexed access, so the entity-local tracker must see through it.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var o = orders?[0];
        Console.WriteLine({|#0:o.Customer|}.Name);
    }
}
" + MockNamespace;

        var expected = Diagnostic(0, "Customer", "Order");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
