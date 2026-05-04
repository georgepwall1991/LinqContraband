using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

public class GroupByNonTranslatableTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task GroupBy_Select_ToList_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { Key = g.Key, Items = {|LC024:g.ToList()|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_Where_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:g.Where(x => x.Amount > 0)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_First_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:g.First()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_CustomMethodNamedCountWithGroup_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:Count(g)|});
        }

        private static int Count(IGrouping<int, Order> group) => group.Count();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_LocalHelperOverKey_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public string CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:Normalize(g.Key)|});
        }

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_StringComparisonOverKey_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public string CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:g.Key.Equals(""vip"", StringComparison.OrdinalIgnoreCase)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_ObjectConstructionWithGroup_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }
    public sealed class Bucket
    {
        public Bucket(IGrouping<int, Order> group) { }
    }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new Bucket({|LC024:g|}));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_NestedNonTranslatableProjection_ShouldTriggerLC024()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { Summary = new { First = {|LC024:g.First()|} } });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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
    public async Task GroupBy_Select_KeyAndAggregates_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { Key = g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_AllSupportedAggregates_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new
                {
                    Key = g.Key,
                    Count = g.Count(),
                    LongCount = g.LongCount(),
                    Sum = g.Sum(o => o.Amount),
                    Average = g.Average(o => o.Amount),
                    Min = g.Min(o => o.Amount),
                    Max = g.Max(o => o.Amount)
                });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_StaticEnumerableAggregates_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new
                {
                    Key = g.Key,
                    Count = Enumerable.Count(g),
                    Total = Enumerable.Sum(g, o => o.Amount)
                });
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

    [Fact]
    public async Task GroupBy_Select_KeyOnly_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => g.Key);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EnumerableGroupBy_Select_ClientProjection_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public class TestClass
    {
        public void TestMethod(IEnumerable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { Key = g.Key, Items = g.ToList() });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
