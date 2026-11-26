using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

/// <summary>
/// Leftover LC024 translatable-operator, aggregate, EF8 terminal, and
/// <c>System.Convert</c> names the shipped fixtures do not isolate.
/// <c>Where</c>/<c>Select</c>/<c>OrderBy</c> already sit in quiet aggregate
/// chains; <c>Any</c>, <c>Count</c>, <c>Sum</c>, <c>Min</c>, <c>Max</c>,
/// <c>Average</c> and <c>LongCount</c> already sit in
/// <c>AllSupportedAggregates</c>; <c>ToBoolean</c>/<c>ToInt32</c> already sit
/// in the Bitwarden conversion fixtures. Dropping a leftover operator from
/// <c>TranslatableGroupOperators</c> FNs only that pin because the chain then
/// stops before <c>Count</c>. Dropping <c>All</c> FNs only
/// <see cref="LeftoverAll_StaysQuiet"/>. Dropping an EF8 terminal from
/// <c>GroupProjectionTerminals</c> FNs only that quiet pin; <c>First</c> and
/// <c>ToList</c> in <c>GroupShapes</c> stay green. Dropping a leftover
/// <c>Convert.To*</c> name FNs only that inner-selector pin.
/// <c>ThenBy</c> sits after <c>OrderBy</c>; dropping <c>OrderBy</c> also FNs
/// the ThenBy pins. A lookalike <c>ThenBy</c> still reports so a later edit
/// that treats every matching name as a group operator is visible.
/// Async aggregate names stay unpinned: <c>IGrouping</c> is not
/// <c>IQueryable</c>, so they do not bind on a real group parameter.
/// </summary>
public partial class GroupByNonTranslatableTests
{
    private static string LeftoverOrdersBody(string projection) => @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } public int Flag { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { Key = g.Key, Value = " + projection + @" });
        }
    }
}";

    private static Task VerifyLeftoverQuietAsync(string projection) =>
        VerifyCS.VerifyAnalyzerAsync(Usings + LeftoverOrdersBody(projection));

    private static Task VerifyLeftoverEf8QuietAsync(string projection) =>
        VerifyCS.VerifyAnalyzerAsync(EfCoreVersion("8.0.0.0") + LeftoverOrdersBody(projection));

    [Fact]
    public Task LeftoverThenBy_ThenCount_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.OrderBy(o => o.Flag).ThenBy(o => o.Amount).Count()");

    [Fact]
    public Task LeftoverThenByDescending_ThenCount_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.OrderBy(o => o.Flag).ThenByDescending(o => o.Amount).Count()");

    [Fact]
    public Task LeftoverDistinct_ThenCount_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Distinct().Count()");

    [Fact]
    public Task LeftoverAll_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.All(o => o.Amount > 0)");

    [Fact]
    public Task LeftoverLastOrDefault_AfterOrderBy_StaysQuietOnEf8() =>
        VerifyLeftoverEf8QuietAsync("g.OrderBy(o => o.Flag).LastOrDefault()");

    [Fact]
    public Task LeftoverSingleOrDefault_StaysQuietOnEf8() =>
        VerifyLeftoverEf8QuietAsync("g.SingleOrDefault()");

    [Fact]
    public Task LeftoverElementAt_StaysQuietOnEf8() =>
        VerifyLeftoverEf8QuietAsync("g.ElementAt(0)");

    [Fact]
    public Task LeftoverElementAtOrDefault_StaysQuietOnEf8() =>
        VerifyLeftoverEf8QuietAsync("g.ElementAtOrDefault(0)");

    [Fact]
    public Task LeftoverToArray_StaysQuietOnEf8() =>
        VerifyLeftoverEf8QuietAsync("g.ToArray()");

    [Fact]
    public Task LeftoverConvertToByte_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToByte(c.Flag))");

    [Fact]
    public Task LeftoverConvertToDecimal_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToDecimal(c.Amount))");

    [Fact]
    public Task LeftoverConvertToDouble_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToDouble(c.Amount))");

    [Fact]
    public Task LeftoverConvertToInt16_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToInt16(c.Flag))");

    [Fact]
    public Task LeftoverConvertToInt64_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToInt64(c.Flag))");

    [Fact]
    public Task LeftoverConvertToString_InsideAggregate_StaysQuiet() =>
        VerifyLeftoverQuietAsync("g.Min(c => Convert.ToString(c.Amount))");

    [Fact]
    public async Task LookalikeThenBy_OnUserGrouping_Reports()
    {
        var test = Usings + @"
namespace TestApp
{
    public class Order { public int CustomerId { get; set; } public decimal Amount { get; set; } }

    public static class GroupLooks
    {
        public static int ThenBy<T>(IGrouping<int, T> group) => group.Count();
    }

    public class TestClass
    {
        public void TestMethod(IQueryable<Order> orders)
        {
            var result = orders
                .GroupBy(o => o.CustomerId)
                .Select(g => {|LC024:GroupLooks.ThenBy(g)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
