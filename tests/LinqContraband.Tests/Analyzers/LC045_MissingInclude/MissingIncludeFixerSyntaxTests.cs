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
    public async Task FixCrime_StaticCallSyntax_WrapsTheArgumentNotTheTypeName()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = System.Linq.Enumerable.ToList(db.Orders);
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
        var orders = System.Linq.Enumerable.ToList(db.Orders.Include(x => x.Customer));
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_WidenedEnumerableAlias_DiagnosticOnly()
    {
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
            Console.WriteLine({|#0:o.Customer|}.Name);
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
        IEnumerable<Order> source = db.Orders;
        var orders = source.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Customer", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));
        testObj.FixedState.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithSpan(19, 31, 19, 41)
                .WithArguments("Customer", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_KeywordNamedNavigation_EscapesTheIdentifier()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|LC045:o.@event|}.Name);
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
        var orders = db.Orders.Include(x => x.@event).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.@event.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_AddsIncludeToEveryFlaggedQuery()
    {
        var test = Usings + @"
class Program
{
    void First()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#0:o.Customer|}.Name);
        }
    }

    void Second()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Where(o => o.Id > 0).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine({|#1:o.Customer|}.Name);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void First()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }

    void Second()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Where(o => o.Id > 0).Include(x => x.Customer).ToList();
        foreach (var o in orders)
        {
            Console.WriteLine(o.Customer.Name);
        }
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "LC045_AddInclude:Customer"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Customer", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC045", DiagnosticSeverity.Warning)
                .WithLocation(1)
                .WithArguments("Customer", "Order")
                .WithOptions(DiagnosticOptions.IgnoreAdditionalLocations));

        await testObj.RunAsync();
    }
}
