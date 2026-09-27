using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC057_EmptyQueryAggregate.EmptyQueryAggregateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC057_EmptyQueryAggregate;

/// <summary>
/// Leftover 5.15.0 arms the original 48 analyzer cases do not isolate.
/// Dropping <c>LongCount</c> from <c>IsRowCheck</c> keeps the <c>Any</c>/<c>Count</c>
/// fixtures green and only fails the same-query LongCount pin. Dropping
/// <c>LongCountAsync</c> fails only the async pin. A LongCount on a different
/// query still reports so a later edit that treats any LongCount in the member
/// as a guard is visible. Dropping <c>SqlQuery</c> from <c>IsEfRawQueryRoot</c>
/// fails only that pin; dropping <c>SqlQueryRaw</c> fails only the raw pin.
/// <c>FromSql</c> roots and caller-side LongCount guards are documented FNs
/// and are not locked as quiet.
/// </summary>
public partial class EmptyQueryAggregateTests
{
    [Fact]
    public async Task LongCount_OnSameQuery_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Products.Where(p => p.CategoryId == categoryId);
        if (query.LongCount() == 0) return null;
        var max = query.Max(p => p.Price);"));
    }

    [Fact]
    public async Task LongCountAsync_OnSameQuery_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Products.Where(p => p.CategoryId == categoryId);
        if (await query.LongCountAsync(ct) == 0) return null;
        var max = query.Max(p => p.Price);"));
    }

    [Fact]
    public async Task LongCount_OnDifferentQuery_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        if (db.Categories.LongCount() == 0) { }
        var max = db.Products.{|LC057:Max|}(p => p.Price);"));
    }

    [Fact]
    public async Task SqlQuery_ValueType_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var max = db.Database.SqlQuery<decimal>($""SELECT Price FROM Products"").{|LC057:Max|}();"));
    }

    [Fact]
    public async Task SqlQueryRaw_ValueType_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var min = db.Database.SqlQueryRaw<int>(""SELECT Id FROM Products"").{|LC057:Min|}();"));
    }
}
