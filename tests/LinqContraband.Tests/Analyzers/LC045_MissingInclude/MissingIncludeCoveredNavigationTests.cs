using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_LambdaIncludeCoversAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_StringIncludeCoversAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(""Customer"").ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ThenIncludeCoversNestedAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ThenInclude(c => c.Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FilteredIncludeCoversAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items.Where(i => i.Id > 0)).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Items.Count);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NonConstantStringInclude_BailsOnWholeQuery_NoDiagnostic()
    {
        // We cannot prove what the dynamic Include loads, so the entire query is out of scope
        // — even for navigations the dynamic string could not plausibly cover.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var navigation = ""Customer"";
        var orders = db.Orders.Include(navigation).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
            Console.WriteLine(o.Items.Count);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NullForgivingMidPathInclude_NoDiagnostic()
    {
        // o.Customer!.Address is the idiomatic NRT spelling of a multi-level include; the
        // parser must see "Customer.Address", not a truncated "Address".
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer!.Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CastMidPathInclude_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => ((Customer)o.Customer).Address).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
