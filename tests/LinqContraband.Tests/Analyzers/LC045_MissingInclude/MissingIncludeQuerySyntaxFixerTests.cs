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
    public async Task FixCrime_QuerySyntax_AddsIncludeToTheFromClauseSource()
    {
        // Wrapping the lowered identity projection would emit `select o.Include(...)`, where
        // the range variable is an entity rather than a queryable, so it would not compile.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders where o.Id > 0 select o).ToList();
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
        var orders = (from o in db.Orders.Include(x => x.Customer) where o.Id > 0 select o).ToList();
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
    public async Task FixCrime_QuerySyntaxWithoutClauses_AddsIncludeToTheFromClauseSource()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders select o).ToList();
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
        var orders = (from o in db.Orders.Include(x => x.Customer) select o).ToList();
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
    public async Task FixCrime_QuerySyntaxOrderBy_AddsIncludeToTheFromClauseSource()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders orderby o.Id select o).ToList();
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
        var orders = (from o in db.Orders.Include(x => x.Customer) orderby o.Id select o).ToList();
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
    public async Task FixCrime_QuerySyntaxElementMaterializer_AddsIncludeToTheFromClauseSource()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = (from o in db.Orders where o.Id > 0 select o).First();
        Console.WriteLine({|LC045:order.Customer|}.Name);
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = (from o in db.Orders.Include(x => x.Customer) where o.Id > 0 select o).First();
        Console.WriteLine(order.Customer.Name);
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_QuerySyntaxNestedPath_ExtendsTheExistingIncludeChain()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders.Include(o => o.Customer) select o).ToList();
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
        var orders = (from o in db.Orders.Include(o => o.Customer).ThenInclude(x => x.Address) select o).ToList();
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
    public async Task FixCrime_CollectionAliasLocal_AddsIncludeToTheOriginalQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var active = orders.Where(o => o.Id > 0);
        foreach (var o in active)
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
        var orders = db.Orders.Include(x => x.Customer).ToList();
        var active = orders.Where(o => o.Id > 0);
        foreach (var o in active)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InMemoryQuerySyntaxView_AddsIncludeToTheOriginalQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in from x in orders where x.Id > 0 select x)
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
        var orders = db.Orders.Include(x => x.Customer).ToList();
        foreach (var o in from x in orders where x.Id > 0 select x)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixInnocent_QuerySyntaxWithALet_IsNotReportedAtAll()
    {
        // A `let` lowers to a projection onto a transparent identifier rather than to an identity
        // projection, so the chain proof never accepts the query and there is nothing to fix.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders let tag = o.Status where tag != null select o).ToList();
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
    public async Task FixCrime_QuerySyntaxWithAContinuation_OffersNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders select o into kept where kept.Id > 0 select kept).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|LC045:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = test }.RunAsync();
    }

    [Fact]
    public async Task FixInnocent_QuerySyntaxWithAJoin_IsNotReportedAtAll()
    {
        // A join lowers to Queryable.Join, which the chain proof rejects, so no diagnostic and no
        // fix. The fixer's clause guard is a forward guard for exactly this family: if the proof
        // ever widens to accept them, the from-clause rewrite must not be applied blindly.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders join c in db.Customers on o.Id equals c.Id select o).ToList();
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
    public async Task FixInnocent_QuerySyntaxWithASecondFrom_IsNotReportedAtAll()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = (from o in db.Orders from i in o.Items select o).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = test }.RunAsync();
    }
}
