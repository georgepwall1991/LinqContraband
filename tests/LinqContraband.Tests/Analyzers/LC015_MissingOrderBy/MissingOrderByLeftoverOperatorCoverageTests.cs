using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer>;
using LinqContraband.Analyzers.LC015_MissingOrderBy;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Leftover LC015 pagination and sort names the shipped fixtures do not isolate.
/// <c>Skip</c>, <c>Take</c>, <c>Last</c>, <c>LastAsync</c>, <c>ElementAt</c>,
/// <c>ElementAtOrDefault</c> and <c>ElementAtAsync</c> already have crime cases.
/// <c>OrderBy</c>, <c>OrderByDescending</c> and <c>ThenBy</c> already protect a
/// later page. <c>LastOrDefault</c>, <c>LastOrDefaultAsync</c>,
/// <c>ElementAtOrDefaultAsync</c> and <c>Chunk</c> are listed beside them, but no
/// fixture marks them. A helper whose body only calls <c>ThenByDescending</c>
/// is the leftover <c>SortingMethods</c> name: dropping it makes the following
/// <c>Skip</c> report, while the shipped <c>ThenBy</c> window stays green.
/// Extra async and <c>Chunk</c> stubs live in this file only.
/// <c>TakeLast</c>/<c>SkipLast</c> stay a documented non-goal.
/// </summary>
public partial class MissingOrderByTests
{
    private const string LeftoverAsyncMocks = @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<T> LastOrDefaultAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(source.LastOrDefault());

        public static Task<T> ElementAtOrDefaultAsync<T>(this IQueryable<T> source, int index) =>
            Task.FromResult(source.ElementAtOrDefault(index));
    }
}
";

    private const string LeftoverChunkMock = @"
namespace LeftoverLinq
{
    public static class LeftoverPaginationExtensions
    {
        public static System.Collections.Generic.IEnumerable<T[]> Chunk<T>(this IQueryable<T> source, int size) =>
            null;
    }
}
";

    private static string WrapLeftover(string body, string extra = "", bool async = false)
    {
        var signature = async
            ? "public async System.Threading.Tasks.Task Main()"
            : "public void Main()";

        return Usings + extra + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        " + signature + @"
        {
            using var db = new AppDbContext();
            " + body + @"
        }
    }
}" + MockNamespace;
    }

    [Fact]
    public async Task LastOrDefault_WithoutOrderBy_Reports()
    {
        var test = WrapLeftover("var user = db.Users.{|#0:LastOrDefault|}();");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("LastOrDefault"));
    }

    [Fact]
    public async Task LastOrDefaultAsync_WithoutOrderBy_Reports()
    {
        var test = WrapLeftover(
            "var user = await db.Users.{|#0:LastOrDefaultAsync|}();",
            LeftoverAsyncMocks,
            async: true);

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("LastOrDefaultAsync"));
    }

    [Fact]
    public async Task ElementAtOrDefaultAsync_WithoutOrderBy_Reports()
    {
        var test = WrapLeftover(
            "var user = await db.Users.{|#0:ElementAtOrDefaultAsync|}(5);",
            LeftoverAsyncMocks,
            async: true);

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("ElementAtOrDefaultAsync"));
    }

    [Fact]
    public async Task Chunk_WithoutOrderBy_Reports()
    {
        var test = WrapLeftover(
            "var pages = db.Users.{|#0:Chunk|}(10);",
            "using LeftoverLinq;" + LeftoverChunkMock);

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("Chunk"));
    }

    [Fact]
    public async Task LookalikeLastOrDefaultAsync_OnEfReceiver_StillReports()
    {
        const string lookalike = @"
using System.Threading.Tasks;

namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static Task<T> LastOrDefaultAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(source.LastOrDefault());
    }
}
";
        var test = WrapLeftover(
            "var user = await OtherOrm.QueryExtensions.{|#0:LastOrDefaultAsync|}(db.Users);",
            lookalike,
            async: true);

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("LastOrDefaultAsync"));
    }

    [Fact]
    public async Task ThenByDescending_InSortHelper_ThenSkip_StaysQuiet()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var page = ApplyTieBreaker(db.Users).Skip(10);
        }

        static IQueryable<User> ApplyTieBreaker(IQueryable<User> query) =>
            ((IOrderedQueryable<User>)query).ThenByDescending(u => u.Id);
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
