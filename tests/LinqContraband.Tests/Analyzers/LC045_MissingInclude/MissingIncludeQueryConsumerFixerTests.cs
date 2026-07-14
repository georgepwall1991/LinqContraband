using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeFixerTests
{
    [Fact]
    public async Task FixCrime_InlineListForEach_AnchorsBeforeOriginalMaterializer()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        db.Orders.ToList().ForEach(order => Console.WriteLine({|LC045:order.Customer|}.Name));
    }
}
"
            + MockNamespace;

        var fixedCode =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        db.Orders.Include(x => x.Customer).ToList().ForEach(order => Console.WriteLine(order.Customer.Name));
    }
}
"
            + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_ExactListForEach_AnchorsBeforeOriginalMaterializer()
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
        orders.ForEach(order => Console.WriteLine({|LC045:order.Customer|}.Name));
    }
}
"
            + MockNamespace;

        var fixedCode =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToList();
        orders.ForEach(order => Console.WriteLine(order.Customer.Name));
    }
}
"
            + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_ReorderedStaticElementAt_AnchorsOnlySemanticSource()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = System.Linq.Queryable.ElementAt(index: 0, source: db.Orders);
        Console.WriteLine({|LC045:order.Customer|}.Name);
    }
}
"
            + MockNamespace;

        var fixedCode =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var order = System.Linq.Queryable.ElementAt(index: 0, source: db.Orders.Include(x => x.Customer));
        Console.WriteLine(order.Customer.Name);
    }
}
"
            + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_ToHashSetMaterializer_AddsIncludeAndCompiles()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToHashSet();
        foreach (var order in orders)
        {
            Console.WriteLine({|LC045:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        var fixedCode =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToHashSet();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        var testObj = new CodeFixTest { TestCode = test, FixedCode = fixedCode };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_ToHashSetComparer_AddsIncludeBeforeMaterializer()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToHashSet(EqualityComparer<Order>.Default);
        foreach (var order in orders)
        {
            Console.WriteLine({|LC045:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        var fixedCode =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(x => x.Customer).ToHashSet(EqualityComparer<Order>.Default);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }
}
