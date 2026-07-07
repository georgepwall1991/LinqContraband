using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

public partial class GroupByNonTranslatableTests
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
    public async Task GroupBy_ResultSelector_ToList_ShouldTriggerLC024()
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
                .GroupBy(o => o.CustomerId, (key, group) => new { Key = key, Items = {|LC024:group.ToList()|} });
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
    public async Task GroupBy_Select_Any_ShouldNotTrigger()
    {
        // EF Core translates g.Any() in a grouping projection (EXISTS / COUNT(*) > 0).
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
                .Select(g => new { Key = g.Key, Has = g.Any() });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_WhereThenCount_ShouldNotTrigger()
    {
        // EF Core 9 translates a filtered group aggregate (COUNT over a CASE/filter). The Where is
        // an intermediate operator enclosed by the Count aggregate, so the chain is translatable.
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
                .Select(g => new { Key = g.Key, Positive = g.Where(o => o.Amount > 0).Count() });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_SelectThenSum_ShouldNotTrigger()
    {
        // g.Select(o => o.Amount).Sum() is equivalent to g.Sum(o => o.Amount); EF emits SUM(Amount).
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
                .Select(g => new { Key = g.Key, Total = g.Select(o => o.Amount).Sum() });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_OrderByThenFirst_ShouldTriggerLC024()
    {
        // Guardrail: an intermediate operator that terminates in an element accessor (First), not an
        // aggregate, is still non-translatable and must report.
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
                .Select(g => new { Key = g.Key, Top = {|LC024:g.OrderBy(o => o.Amount).First()|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_SelectThenToList_ShouldTriggerLC024()
    {
        // Guardrail: a chain that terminates in a materializer (ToList), not an aggregate, must report.
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
                .Select(g => new { Key = g.Key, Amounts = {|LC024:g.Select(o => o.Amount).ToList()|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_UserMethodSelectorThenSum_ShouldTriggerLC024()
    {
        // Guardrail: the chain ends in an aggregate, but the Select selector calls a user-defined
        // method EF cannot translate, so the chain is not actually translatable and must report.
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
                .Select(g => new { Key = g.Key, Total = {|LC024:g.Select(o => Scale(o.Amount)).Sum()|} });
        }

        private static decimal Scale(decimal value) => value * 2;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_BclMethodInPredicateThenCount_ShouldTriggerLC024()
    {
        // Guardrail: a method call in the predicate — even a BCL one — is not assumed translatable.
        // The chain exemption only covers invocation-free predicates/selectors, so this reports.
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
                .Select(g => new { Key = g.Key, C = {|LC024:g.Where(o => o.Amount.ToString().StartsWith(""1"")).Count()|} });
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
