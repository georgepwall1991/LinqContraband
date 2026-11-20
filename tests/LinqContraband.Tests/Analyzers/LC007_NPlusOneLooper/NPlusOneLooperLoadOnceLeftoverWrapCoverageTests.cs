using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Remaining LC007 load-once <c>ElementPreservingOperators</c> names that
/// <c>NPlusOneLooperLoadOnceLeftoverOperatorCoverageTests</c> (draft #720)
/// does not isolate. Dropping leftover <c>SkipWhile</c> fails only
/// <see cref="SkipWhile_LoadOnce_StaysQuiet"/>; the shipped <c>Where.First</c>
/// load-once stays green. The same holds for leftover <c>TakeWhile</c>,
/// <c>SkipLast</c>, <c>TakeLast</c>, <c>AsTracking</c>, <c>AsSingleQuery</c>,
/// <c>IgnoreAutoIncludes</c> and <c>TagWithCallSite</c>.
/// <c>AsQueryable</c> stays unpinned: dropping it still leaves
/// <c>db.Users.AsQueryable().First()</c> quiet, so the name is not isolated.
/// <c>AsAsyncEnumerable</c> stays unpinned: the shared mock returns
/// <c>IAsyncEnumerable&lt;T&gt;</c>, so <c>First()</c> after it does not bind.
/// <c>ThenBy</c>/<c>ThenByDescending</c> stay unpinned: they require
/// <c>OrderBy</c>. <c>ThenInclude</c> stays unpinned because it is reached
/// through <c>Include</c>. A leftover lookalike name on the EF receiver
/// still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string LeftoverLoadOnceWrapExtensions = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class LeftoverLoadOnceWrapExtensions
    {
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsSingleQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreAutoIncludes<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> TagWithCallSite<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> LookalikeAsTracking<T>(this IQueryable<T> source) => source;
    }
}
";

    private static string LeftoverLoadOnceWrapProgram(string body) =>
        LoopProgram(body) + LeftoverLoadOnceWrapExtensions;

    private static string LoadOnceWrapQuery(string query) => @"
User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = " + query + @";
            Console.WriteLine(first.Id + id);
        }";

    private static Task VerifyLeftoverWrapQuietAsync(string query) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverLoadOnceWrapProgram(LoadOnceWrapQuery(query)));

    [Fact]
    public Task SkipWhile_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.SkipWhile(u => false).First()");

    [Fact]
    public Task TakeWhile_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.TakeWhile(u => true).First()");

    [Fact]
    public Task SkipLast_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.SkipLast(0).First()");

    [Fact]
    public Task TakeLast_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.TakeLast(1).First()");

    [Fact]
    public Task AsTracking_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.AsTracking().First()");

    [Fact]
    public Task AsSingleQuery_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.AsSingleQuery().First()");

    [Fact]
    public Task IgnoreAutoIncludes_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.IgnoreAutoIncludes().First()");

    [Fact]
    public Task TagWithCallSite_LoadOnce_StaysQuiet() =>
        VerifyLeftoverWrapQuietAsync("db.Users.TagWithCallSite().First()");

    [Fact]
    public Task LookalikeAsTracking_OnEfReceiver_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverLoadOnceWrapProgram(@"
User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = {|LC007:db.Users.LookalikeAsTracking().First()|};
            Console.WriteLine(first.Id + id);
        }"));
}
