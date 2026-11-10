using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover LC007 load-once <c>ElementPreservingOperators</c> names the shipped
/// <c>Where</c>/<c>First</c> fixtures do not isolate. Dropping leftover
/// <c>OrderBy</c> FNs only
/// <see cref="OrderBy_LoadOnce_StaysQuiet"/>; the shipped <c>Where.First</c>
/// load-once stays green. The same holds for leftover
/// <c>OrderByDescending</c>, <c>Skip</c>, <c>Take</c>, <c>Distinct</c>,
/// <c>Reverse</c>, <c>OfType</c>, <c>AsEnumerable</c>, <c>AsNoTracking</c>,
/// <c>AsNoTrackingWithIdentityResolution</c>, <c>Include</c>,
/// <c>IgnoreQueryFilters</c>, <c>AsSplitQuery</c> and <c>TagWith</c>.
/// <c>ThenBy</c>/<c>ThenByDescending</c> stay unpinned: they require
/// <c>OrderBy</c>, which is also in the set, so they cannot isolate.
/// <c>ThenInclude</c> stays unpinned because it is reached through
/// <c>Include</c>. <c>Where</c> is already isolated by the shipped load-once
/// fixture. A leftover lookalike name on the EF receiver still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string LeftoverLoadOnceExtensions = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class LeftoverLoadOnceExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => source;
        public static IQueryable<T> LookalikeAsNoTracking<T>(this IQueryable<T> source) => source;
    }
}
";

    private static string LeftoverLoadOnceProgram(string body) => LoopProgram(body) + LeftoverLoadOnceExtensions;

    private static string LoadOnceQuery(string query) => @"
User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = " + query + @";
            Console.WriteLine(first.Id + id);
        }";

    private static Task VerifyLeftoverQuietAsync(string query) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverLoadOnceProgram(LoadOnceQuery(query)));

    [Fact]
    public Task OrderBy_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.OrderBy(u => u.Id).First()");

    [Fact]
    public Task OrderByDescending_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.OrderByDescending(u => u.Id).First()");

    [Fact]
    public Task Skip_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.Skip(0).First()");

    [Fact]
    public Task Take_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.Take(1).First()");

    [Fact]
    public Task Distinct_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.Distinct().First()");

    [Fact]
    public Task Reverse_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.Reverse().First()");

    [Fact]
    public Task OfType_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.OfType<User>().First()");

    [Fact]
    public Task AsEnumerable_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.AsEnumerable().First()");

    [Fact]
    public Task AsNoTracking_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.AsNoTracking().First()");

    [Fact]
    public Task AsNoTrackingWithIdentityResolution_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.AsNoTrackingWithIdentityResolution().First()");

    [Fact]
    public Task Include_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.Include(u => u.Profile).First()");

    [Fact]
    public Task IgnoreQueryFilters_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.IgnoreQueryFilters().First()");

    [Fact]
    public Task AsSplitQuery_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.AsSplitQuery().First()");

    [Fact]
    public Task TagWith_LoadOnce_StaysQuiet() =>
        VerifyLeftoverQuietAsync("db.Users.TagWith(\"root\").First()");

    [Fact]
    public Task LookalikeAsNoTracking_OnEfReceiver_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverLoadOnceProgram(@"
User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = {|LC007:db.Users.LookalikeAsNoTracking().First()|};
            Console.WriteLine(first.Id + id);
        }"));
}
