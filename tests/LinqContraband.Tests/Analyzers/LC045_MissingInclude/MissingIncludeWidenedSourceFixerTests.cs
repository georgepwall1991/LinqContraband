using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeFixerTests
{

    [Fact]
    public async Task FixCrime_WidenedEnumerableLocal_AddsIncludeWhereTheQueryWasAssigned()
    {
        // Include is declared on IQueryable<T>, so it cannot go where the widened local is
        // consumed. It can go where the local was given the query, and the result still converts
        // to IEnumerable<Order> because Include returns an IIncludableQueryable<T, P>.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders;
        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders.Include(x => x.Customer);
        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_WidenedEnumerableLocalNestedPath_AddsIncludeThenInclude()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders.Include(o => o.Customer);
        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|LC045:o.Customer.Address|}.City);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders.Include(o => o.Customer).ThenInclude(x => x.Address);
        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Address.City);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixInnocent_ReassignedWidenedLocal_IsNotReportedAtAll()
    {
        // The analyzer already declines a reassigned source, so the fixer's never-reassigned
        // requirement is a forward guard rather than a reachable branch.
        var test = Usings + @"
class Program
{
    void Main(bool flag)
    {
        var db = new MyDbContext();
        IEnumerable<Order> source = db.Orders;
        if (flag)
            source = new List<Order>();

        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = test }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_WidenedLocalFromAMaterializedCollection_FixesTheOriginalQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var materialized = db.Orders.ToList();
        IEnumerable<Order> source = materialized;
        foreach (var o in source)
        {
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var materialized = db.Orders.Include(x => x.Customer).ToList();
        IEnumerable<Order> source = materialized;
        foreach (var o in source)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }
}
