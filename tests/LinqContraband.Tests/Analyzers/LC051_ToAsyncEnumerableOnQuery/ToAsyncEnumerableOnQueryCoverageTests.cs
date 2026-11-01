using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery.ToAsyncEnumerableOnQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC051_ToAsyncEnumerableOnQuery;

/// <summary>
/// Leftover 5.10.0 LC051 arms the original 12 analyzer cases do not isolate.
/// <c>AsNoTracking</c> already covers the <c>Microsoft.EntityFrameworkCore</c>
/// namespace check in <c>IsQueryOperator</c>. Restricting that arm to
/// <c>AsNoTracking</c> by name FNs only these Include/TagWith pins;
/// <c>QueryChain_Reports</c> stays green. A same-named <c>Include</c> outside
/// that namespace must stay quiet: matching Include by name alone FPs only
/// that lookalike. Queries stored in locals remain a documented FN; do not
/// lock them as quiet.
/// </summary>
public partial class ToAsyncEnumerableOnQueryTests
{
    [Fact]
    public async Task Include_ThenToAsyncEnumerable_Reports()
    {
        // Removing Include from the EF mock, or limiting IsQueryOperator to
        // AsNoTracking, stops the walk before the DbSet.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Orders.Include(o => o.Total).{|LC051:ToAsyncEnumerable|}();"));
    }

    [Fact]
    public async Task TagWith_ThenToAsyncEnumerable_Reports()
    {
        // TagWith is the other documented EF-namespace operator; AsNoTracking
        // does not prove this name is walked.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Orders.TagWith(""orders"").{|LC051:ToAsyncEnumerable|}();"));
    }

    [Fact]
    public async Task ProjectIncludeLookalike_StaysQuiet()
    {
        // A project Include in another namespace is not an EF query operator.
        // Matching Include by name, without the EF namespace check, reports here.
        var lookalike = @"
namespace TestApp.Lookalikes
{
    public static class QueryLookalikes
    {
        public static System.Linq.IQueryable<T> Include<T>(this System.Linq.IQueryable<T> source, string path) => source;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"
            var stream = db.Orders.Include(""Total"").ToAsyncEnumerable();",
                usings: "    using Microsoft.EntityFrameworkCore;\n    using TestApp.Lookalikes;\n")
            + lookalike);
    }
}
