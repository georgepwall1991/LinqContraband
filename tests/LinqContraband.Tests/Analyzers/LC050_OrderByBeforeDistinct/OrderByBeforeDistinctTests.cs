using LinqContraband.Tests.Analyzers.LC049_IncludeIgnoredByProjection;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC050_OrderByBeforeDistinct.OrderByBeforeDistinctAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC050_OrderByBeforeDistinct;

public class OrderByBeforeDistinctTests
{
    internal static string Wrap(string body) => IncludeIgnoredByProjectionTests.EFCoreMock + @"
namespace TestApp
{
    class Program
    {
        void Run(AppDbContext db, IQueryable<Order> orders, List<Order> cached)
        {
" + body + @"
        }
    }
}";

    [Fact]
    public async Task OrderByThenDistinct_Reports()
    {
        var test = Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Message_NamesTheDiscardedSort()
    {
        var test = Wrap(@"
            var result = db.Orders.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Distinct().ToList();");

        var expected = VerifyCS.Diagnostic("LC050")
            .WithSpan(128, 89, 128, 97)
            .WithArguments("OrderByDescending");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OrderBySelectDistinct_Reports()
    {
        var test = Wrap(@"
            var names = db.Orders
                .OrderBy(o => o.Customer.Name)
                .Select(o => o.Customer.Name)
                .{|LC050:Distinct|}()
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PassThroughOperatorsBetween_Report()
    {
        var test = Wrap(@"
            var result = orders
                .OrderBy(o => o.Total)
                .Where(o => o.Total > 0)
                .AsNoTracking()
                .TagWith(""distinct totals"")
                .Select(o => o.Total)
                .{|LC050:Distinct|}()
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxOrderBy_Reports()
    {
        var test = Wrap(@"
            var names = (from o in db.Orders
                         orderby o.Customer.Name
                         select o.Customer.Name).{|LC050:Distinct|}().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticDistinctCall_Reports()
    {
        var test = Wrap(@"
            var result = Queryable.{|LC050:Distinct|}(db.Orders.OrderBy(o => o.Id)).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderByAfterDistinct_DoesNotReport()
    {
        var test = Wrap(@"
            var result = db.Orders.Select(o => o.Customer.Name).Distinct().OrderBy(n => n).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TakeBetweenOrderByAndDistinct_DoesNotReport()
    {
        // Take keeps the ordering meaningful: it decides which rows reach Distinct.
        var test = Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).Take(10).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SkipBetweenOrderByAndDistinct_DoesNotReport()
    {
        var test = Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).Skip(5).Select(o => o.Id).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InMemoryEnumerable_DoesNotReport()
    {
        var test = Wrap(@"
            var result = cached.OrderBy(o => o.PlacedAt).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsQueryableOverMemory_DoesNotReport()
    {
        var test = Wrap(@"
            var result = cached.AsQueryable().OrderBy(o => o.PlacedAt).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DistinctWithComparer_DoesNotReport()
    {
        var test = Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).Select(o => o.Customer.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupByBetween_DoesNotReport()
    {
        var test = Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).GroupBy(o => o.Id).Select(g => g.Key).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SortedLocal_DoesNotReport()
    {
        var test = Wrap(@"
            var sorted = db.Orders.OrderBy(o => o.PlacedAt);
            var result = sorted.Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DistinctWithoutSort_DoesNotReport()
    {
        var test = Wrap(@"
            var result = db.Orders.Where(o => o.Total > 0).Select(o => o.Id).Distinct().ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
