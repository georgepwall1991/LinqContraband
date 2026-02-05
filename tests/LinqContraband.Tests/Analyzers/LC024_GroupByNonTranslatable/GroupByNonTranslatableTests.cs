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
}
