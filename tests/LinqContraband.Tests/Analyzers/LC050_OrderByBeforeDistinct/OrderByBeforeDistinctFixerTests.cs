using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC050_OrderByBeforeDistinct.OrderByBeforeDistinctAnalyzer,
    LinqContraband.Analyzers.LC050_OrderByBeforeDistinct.OrderByBeforeDistinctFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC050_OrderByBeforeDistinct;

public class OrderByBeforeDistinctFixerTests
{
    private static string Wrap(string body) => OrderByBeforeDistinctTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Fact]
    public async Task MovesSortAfterDistinct_SingleLine()
    {
        await VerifyFixAsync(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}().ToList();", @"
            var result = db.Orders.Distinct().OrderBy(o => o.PlacedAt).ToList();");
    }

    [Fact]
    public async Task MovesWholeSortChain_Multiline()
    {
        await VerifyFixAsync(@"
            var result = db.Orders
                .Where(o => o.Total > 0)
                .OrderByDescending(o => o.PlacedAt)
                .ThenBy(o => o.Id)
                .{|LC050:Distinct|}()
                .ToList();", @"
            var result = db.Orders
                .Where(o => o.Total > 0)
                .Distinct()
                .OrderByDescending(o => o.PlacedAt)
                .ThenBy(o => o.Id)
                .ToList();");
    }

    [Fact]
    public async Task SortKeyMatchesProjection_SortsProjectedValue()
    {
        await VerifyFixAsync(@"
            var names = db.Orders
                .OrderBy(o => o.Customer.Name)
                .Select(x => x.Customer.Name)
                .{|LC050:Distinct|}()
                .ToList();", @"
            var names = db.Orders
                .Select(x => x.Customer.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToList();");
    }

    [Fact]
    public async Task DescendingSortKeyMatchesProjection()
    {
        await VerifyFixAsync(@"
            var totals = db.Orders.OrderByDescending(o => o.Total).Select(o => o.Total).{|LC050:Distinct|}().ToList();", @"
            var totals = db.Orders.Select(o => o.Total).Distinct().OrderByDescending(o => o).ToList();");
    }

    [Fact]
    public async Task FixAll_FixesEveryQuery()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
            var a = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}().ToList();
            var b = db.Orders.OrderBy(o => o.Total).Select(o => o.Total).{|LC050:Distinct|}().ToList();"),
            FixedCode = Wrap(@"
            var a = db.Orders.Distinct().OrderBy(o => o.PlacedAt).ToList();
            var b = db.Orders.Select(o => o.Total).Distinct().OrderBy(o => o).ToList();"),
            BatchFixedCode = Wrap(@"
            var a = db.Orders.Distinct().OrderBy(o => o.PlacedAt).ToList();
            var b = db.Orders.Select(o => o.Total).Distinct().OrderBy(o => o).ToList();")
        }.RunAsync();
    }

    [Fact]
    public async Task SortKeyDiffersFromProjection_NoFix()
    {
        await VerifyNoFixAsync(@"
            var names = db.Orders.OrderBy(o => o.PlacedAt).Select(o => o.Customer.Name).{|LC050:Distinct|}().ToList();");
    }

    [Fact]
    public async Task ThenByWithProjection_NoFix()
    {
        await VerifyNoFixAsync(@"
            var names = db.Orders.OrderBy(o => o.Total).ThenBy(o => o.Id).Select(o => o.Total).{|LC050:Distinct|}().ToList();");
    }

    [Fact]
    public async Task WhereBetweenSortAndDistinct_NoFix()
    {
        await VerifyNoFixAsync(@"
            var result = db.Orders.OrderBy(o => o.PlacedAt).Where(o => o.Total > 0).{|LC050:Distinct|}().ToList();");
    }

    [Fact]
    public async Task QuerySyntax_NoFix()
    {
        await VerifyNoFixAsync(@"
            var names = (from o in db.Orders
                         orderby o.Customer.Name
                         select o.Customer.Name).{|LC050:Distinct|}().ToList();");
    }

    [Fact]
    public async Task VarLocalReassignedWithPlainQueryable_NoFix()
    {
        // After the fix the local would be IOrderedQueryable<T>, and the reassignment would stop compiling.
        await VerifyNoFixAsync(@"
            var query = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}();
            query = query.Where(o => o.Total > 0);
            var result = query.ToList();");
    }

    [Fact]
    public async Task VarLocalNotReassigned_Fixes()
    {
        await VerifyFixAsync(@"
            var query = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}();
            var result = query.ToList();", @"
            var query = db.Orders.Distinct().OrderBy(o => o.PlacedAt);
            var result = query.ToList();");
    }

    [Fact]
    public async Task ExplicitlyTypedLocalReassigned_Fixes()
    {
        await VerifyFixAsync(@"
            IQueryable<Order> query = db.Orders.OrderBy(o => o.PlacedAt).{|LC050:Distinct|}();
            query = query.Where(o => o.Total > 0);", @"
            IQueryable<Order> query = db.Orders.Distinct().OrderBy(o => o.PlacedAt);
            query = query.Where(o => o.Total > 0);");
    }
}
