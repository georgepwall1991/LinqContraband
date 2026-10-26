using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection.IncludeIgnoredByProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC049_IncludeIgnoredByProjection;

/// <summary>
/// Leftover 5.9.0 LC049 arms that the original 21 analyzer fixtures do not isolate.
/// Dropping a Queryable name from <c>IsEntityPreservingOperator</c> keeps Where/OrderBy
/// green and fails only that operator's pin. Dropping an EF pass-through name keeps
/// AsNoTracking/TagWith/AsSplitQuery green and fails only that pin. Removing the
/// <c>IFieldReferenceOperation</c> arm keeps property-navigation quiet cases green
/// and only fails the field-navigation pin (it then reports). Ternary and coalesce
/// entity values are already reached by descendant walking of the branch operands,
/// and the DTO fixture already requires <c>IsComparedWithNull</c>, so those are not
/// pinned here. Includes stored in locals remain a documented FN; do not lock as quiet.
/// </summary>
public partial class IncludeIgnoredByProjectionTests
{
    [Fact]
    public async Task Reverse_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).Reverse().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task Distinct_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).Distinct().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task Skip_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).Skip(1).Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task Take_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).Take(10).Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task ThenBy_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders
                .{|LC049:Include|}(o => o.Customer)
                .OrderBy(o => o.PlacedAt)
                .ThenBy(o => o.Id)
                .Select(o => o.Id)
                .ToList();"));
    }

    [Fact]
    public async Task ThenByDescending_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders
                .{|LC049:Include|}(o => o.Customer)
                .OrderBy(o => o.PlacedAt)
                .ThenByDescending(o => o.Id)
                .Select(o => o.Id)
                .ToList();"));
    }

    [Fact]
    public async Task OrderByDescending_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).OrderByDescending(o => o.PlacedAt).Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task AsTracking_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).AsTracking().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).AsNoTrackingWithIdentityResolution().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task TagWithCallSite_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).TagWithCallSite().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task IgnoreQueryFilters_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).IgnoreQueryFilters().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).IgnoreAutoIncludes().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task AsSingleQuery_ThenSelect_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(o => o.Customer).AsSingleQuery().Select(o => o.Id).ToList();"));
    }

    [Fact]
    public async Task FieldNavigationProjection_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var customers = db.Orders.Include(o => o.Customer).Select(o => o.CustomerField).ToList();"));
    }
}
