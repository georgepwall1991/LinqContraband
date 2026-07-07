using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

public partial class GroupByNonTranslatableTests
{
    [Fact]
    public async Task QuerySyntaxGroupBy_Select_ToList_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result =
                from order in orders
                group order by order.CustomerId into g
                select new { Key = g.Key, Items = {|LC024:g.ToList()|} };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxGroupBy_Select_First_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result =
                from order in orders
                group order by order.CustomerId into g
                select {|LC024:g.First()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxGroupBy_Select_SelectThenSum_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result =
                from order in orders
                group order by order.CustomerId into g
                select new { Key = g.Key, Total = g.Select(o => o.Amount).Sum() };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxGroupBy_Select_KeyAndAggregates_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result =
                from order in orders
                group order by order.CustomerId into g
                select new { Key = g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EnumerableQuerySyntaxGroupBy_Select_ClientProjection_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IEnumerable<Order> orders)
        {
            var result =
                from order in orders
                group order by order.CustomerId into g
                select new { Key = g.Key, Items = g.ToList() };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
