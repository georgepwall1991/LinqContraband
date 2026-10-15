using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC050_OrderByBeforeDistinct.OrderByBeforeDistinctAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC050_OrderByBeforeDistinct;

/// <summary>
/// Leftover 5.9.0 LC050 arms that the original 15 analyzer fixtures do not isolate.
/// <c>PassThroughOperatorsBetween_Report</c> already requires <c>AsNoTracking</c> and
/// <c>TagWith</c>. Dropping any other name from <c>IsEfPassThrough</c> keeps that fixture
/// green and fails only that operator's pin. Dropping <c>ThenByDescending</c> from the
/// Queryable walk keeps <c>ThenBy</c> in <c>Message_NamesTheDiscardedSort</c> green and
/// fails only the descending pin. Sorted locals remain a documented FN; do not lock as quiet.
/// </summary>
public class OrderByBeforeDistinctCoverageTests
{
    private static string Wrap(string body) => OrderByBeforeDistinctTests.Wrap(body);

    [Fact]
    public async Task ThenByDescending_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).ThenByDescending(o => o.Id).{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task Include_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).Include(o => o.Customer).{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task ThenInclude_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders
                .OrderBy(o => o.PlacedAt)
                .Include(o => o.Lines)
                .ThenInclude(l => l.Product)
                .{|LC050:Distinct|}()
                .ToList();"));
    }

    [Fact]
    public async Task AsTracking_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).AsTracking().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).AsNoTrackingWithIdentityResolution().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task TagWithCallSite_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).TagWithCallSite().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task IgnoreQueryFilters_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).IgnoreQueryFilters().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).IgnoreAutoIncludes().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task AsSplitQuery_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).AsSplitQuery().{|LC050:Distinct|}().ToList();"));
    }

    [Fact]
    public async Task AsSingleQuery_ThenDistinct_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).AsSingleQuery().{|LC050:Distinct|}().ToList();"));
    }
}
