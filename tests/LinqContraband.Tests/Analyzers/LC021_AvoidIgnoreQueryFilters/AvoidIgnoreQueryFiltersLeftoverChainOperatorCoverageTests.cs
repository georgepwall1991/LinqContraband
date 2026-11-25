using FixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer,
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC021_AvoidIgnoreQueryFilters;

/// <summary>
/// Leftover LC021 <c>ChainOperators</c> names the shipped deleted-rows fixtures
/// do not isolate. <c>Where</c>, <c>OrderBy</c>, <c>Skip</c>, <c>Take</c>,
/// <c>OfType</c>, <c>Cast</c> and <c>FromSqlRaw</c> already withhold the fix.
/// Dropping a leftover name FNs only that pin: the walk stops, so a restore
/// query is offered a fix that would empty it. <c>ThenBy</c> sits after
/// <c>OrderBy</c>; dropping <c>OrderBy</c> also FNs the ThenBy pins.
/// <c>ThenInclude</c> is mocked on <c>IQueryable</c> so it isolates without
/// <c>Include</c>. A lookalike <c>AsNoTracking</c> keeps the fix so a later
/// edit that treats every matching name as a chain operator stays visible.
/// </summary>
public partial class AvoidIgnoreQueryFiltersTests
{
    [Fact]
    public Task LeftoverOrderByDescending_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".OrderByDescending(p => p.Id)");

    [Fact]
    public Task LeftoverThenBy_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".OrderBy(p => p.Id).ThenBy(p => p.TenantId)");

    [Fact]
    public Task LeftoverThenByDescending_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".OrderBy(p => p.Id).ThenByDescending(p => p.TenantId)");

    [Fact]
    public Task LeftoverSkipWhile_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".SkipWhile(p => p.Id > 0)");

    [Fact]
    public Task LeftoverTakeWhile_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".TakeWhile(p => p.Id > 0)");

    [Fact]
    public Task LeftoverDistinct_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".Distinct()");

    [Fact]
    public Task LeftoverReverse_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".Reverse()");

    [Fact]
    public Task LeftoverAsQueryable_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsQueryable()");

    [Fact]
    public Task LeftoverAsNoTracking_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsNoTracking()");

    [Fact]
    public Task LeftoverAsNoTrackingWithIdentityResolution_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsNoTrackingWithIdentityResolution()");

    [Fact]
    public Task LeftoverAsTracking_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsTracking()");

    [Fact]
    public Task LeftoverInclude_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".Include(p => p.Id)");

    [Fact]
    public Task LeftoverThenInclude_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".ThenInclude(p => p.Id)");

    [Fact]
    public Task LeftoverAsSplitQuery_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsSplitQuery()");

    [Fact]
    public Task LeftoverAsSingleQuery_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".AsSingleQuery()");

    [Fact]
    public Task LeftoverTagWith_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".TagWith(\"restore\")");

    [Fact]
    public Task LeftoverTagWithCallSite_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".TagWithCallSite()");

    [Fact]
    public Task LeftoverIgnoreAutoIncludes_ReportsWithoutFix() =>
        VerifyLeftoverChainWithoutFix(".IgnoreAutoIncludes()");

    [Fact]
    public Task LeftoverFromSql_ReportsWithoutFix() =>
        VerifyLeftoverFixWithheld(@"
            TestApp.BlogContext db = null;
            var result = {|LC021:db.Set<TestApp.Post>().FromSql($""SELECT * FROM Posts"").Where(p => p.IsDeleted).IgnoreQueryFilters()|}.ToList();");

    [Fact]
    public Task LeftoverFromSqlInterpolated_ReportsWithoutFix() =>
        VerifyLeftoverFixWithheld(@"
            TestApp.BlogContext db = null;
            var result = {|LC021:db.Set<TestApp.Post>().FromSqlInterpolated($""SELECT * FROM Posts"").Where(p => p.IsDeleted).IgnoreQueryFilters()|}.ToList();");

    [Fact]
    public Task LookalikeAsNoTracking_KeepsFix() =>
        VerifyLeftoverFixOffered(@"
            var result = TestApp.LeftoverQueryLooks.AsNoTracking({|LC021:posts.IgnoreQueryFilters()|}).Where(p => p.IsDeleted).ToList();");

    private static Task VerifyLeftoverChainWithoutFix(string chain) =>
        VerifyLeftoverFixWithheld(@"
            var result = {|LC021:posts.IgnoreQueryFilters()|}" + chain + @".Where(p => p.IsDeleted).ToList();");

    private static Task VerifyLeftoverFixWithheld(string body)
    {
        var code = LeftoverChainCode(body);
        return new FixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    private static Task VerifyLeftoverFixOffered(string body)
    {
        var fixedBody = System.Text.RegularExpressions.Regex.Replace(
            body,
            @"\{\|LC021:(?<call>.*?)\|\}",
            match => System.Text.RegularExpressions.Regex.Replace(
                match.Groups["call"].Value,
                @"\.IgnoreQueryFilters\(.*\)$",
                ""));
        return new FixTest
        {
            TestCode = LeftoverChainCode(body),
            FixedCode = LeftoverChainCode(fixedBody)
        }.RunAsync();
    }

    private static string LeftoverChainCode(string body)
    {
        const string extraEf = @"
        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> AsNoTrackingWithIdentityResolution<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> AsTracking<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> Include<TEntity, TProperty>(this IQueryable<TEntity> source, System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> navigation) => source;
        public static IQueryable<TEntity> ThenInclude<TEntity, TProperty>(this IQueryable<TEntity> source, System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> navigation) => source;
        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> AsSingleQuery<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> TagWith<TEntity>(this IQueryable<TEntity> source, string tag) => source;
        public static IQueryable<TEntity> TagWithCallSite<TEntity>(this IQueryable<TEntity> source, string filePath = """", int lineNumber = 0) => source;
        public static IQueryable<TEntity> IgnoreAutoIncludes<TEntity>(this IQueryable<TEntity> source) => source;
";

        const string extraRelational = @"
        public static IQueryable<T> FromSql<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
        public static IQueryable<T> FromSqlInterpolated<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
";

        const string lookalike = @"
    public static class LeftoverQueryLooks
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
    }
";

        return SoftDeleteCode(body)
            .Replace(
                "        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source, IReadOnlyCollection<string> filterKeys) => source;",
                "        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source, IReadOnlyCollection<string> filterKeys) => source;"
                    + extraEf)
            .Replace(
                "        public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;",
                "        public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;"
                    + extraRelational)
            .Replace(
                "    public static class Settings",
                lookalike + "    public static class Settings");
    }
}
