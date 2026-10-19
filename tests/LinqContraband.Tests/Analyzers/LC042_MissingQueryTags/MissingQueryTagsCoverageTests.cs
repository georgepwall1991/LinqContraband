using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC042_MissingQueryTags;

/// <summary>
/// Leftover LC042 terminal, heavy-operator, include, and query-option names the
/// original 24 analyzer cases do not isolate. Dropping a terminal from
/// <c>TerminalMethods</c> FNs only that pin; <c>ToList</c>/<c>Count</c>/<c>Sum</c>
/// fixtures stay green. Dropping <c>GroupJoin</c> from
/// <c>HeavyQueryableOperators</c> drops that chain to weight 2 and stays quiet,
/// while <c>JoinCountsTwice</c> still reports. Dropping a leftover query-option
/// name ends the walk, so a three-operator chain after it goes quiet while
/// <c>QueryOptions_DoNotCount</c> still uses <c>AsNoTracking</c>.
/// <c>LeftJoin</c>/<c>RightJoin</c> are not pinned: the default VerifyCS
/// reference assemblies do not declare those .NET 10 Queryable methods, so the
/// shapes compile as errors and cannot isolate the heavy-operator arm.
/// </summary>
public sealed class MissingQueryTagsCoverageTests
{
    private static string Wrap(string body, string returnType = "object", bool isAsync = false)
    {
        var mock = MissingQueryTagsTests.EfCoreMock
            .Replace(
                "        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;",
                @"        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> AsNoTrackingWithIdentityResolution<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> AsTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> IgnoreAutoIncludes<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static Task<HashSet<TSource>> ToHashSetAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TSource[]> ToArrayAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;")
            .Replace(
                "        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;",
                @"        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> AsSingleQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;");

        return mock + @"
namespace TestApp
{
    using Microsoft.EntityFrameworkCore;

    public class Queries
    {
        public " + (isAsync ? "async System.Threading.Tasks.Task<" + returnType + ">" : returnType) + @" Run(AppDbContext db)
        {
" + body + @"
        }
    }
}";
    }

    [Fact]
    public async Task ToHashSet_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToHashSet|}();"));
    }

    [Fact]
    public async Task ToHashSetAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToHashSetAsync|}();",
            "HashSet<Order>",
            isAsync: true));
    }

    [Fact]
    public async Task LongCount_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:LongCount|}();"));
    }

    [Fact]
    public async Task LongCountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:LongCountAsync|}();",
            "long",
            isAsync: true));
    }

    [Fact]
    public async Task First_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:First|}();"));
    }

    [Fact]
    public async Task FirstOrDefault_Reports()
    {
        // The shipped TagWithCallSite fixture uses FirstOrDefault only after a tag.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:FirstOrDefault|}();"));
    }

    [Fact]
    public async Task FirstAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:FirstAsync|}();",
            "Order",
            isAsync: true));
    }

    [Fact]
    public async Task Single_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Single|}();"));
    }

    [Fact]
    public async Task SingleOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:SingleOrDefault|}();"));
    }

    [Fact]
    public async Task Last_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Last|}();"));
    }

    [Fact]
    public async Task Any_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Any|}();"));
    }

    [Fact]
    public async Task All_Reports()
    {
        // Queryable.All requires a predicate; the three upstream operators already
        // reach the threshold, so this isolates the All name, not HasLambdaArgument.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:All|}(o => o.CustomerId > 0);"));
    }

    [Fact]
    public async Task Min_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Min|}(o => o.Total);"));
    }

    [Fact]
    public async Task Max_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Max|}(o => o.Total);"));
    }

    [Fact]
    public async Task Average_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:Average|}(o => o.Total);"));
    }

    [Fact]
    public async Task ToDictionary_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToDictionary|}(o => o.Id);"));
    }

    [Fact]
    public async Task ToArrayAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToArrayAsync|}();",
            "Order[]",
            isAsync: true));
    }

    [Fact]
    public async Task GroupJoin_ThenWhere_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.GroupJoin(db.Customers, o => o.CustomerId, c => c.Id, (o, cs) => o).Where(o => o.Total > 0).{|LC042:ToList|}();"));
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_ThenFilterSortPage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.AsNoTrackingWithIdentityResolution().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();"));
    }

    [Fact]
    public async Task AsTracking_ThenFilterSortPage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.AsTracking().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();"));
    }

    [Fact]
    public async Task AsSingleQuery_ThenFilterSortPage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.AsSingleQuery().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_ThenFilterSortPage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.IgnoreAutoIncludes().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();"));
    }

    [Fact]
    public async Task ThenInclude_ThenWhere_Reports()
    {
        // ThenInclude is an IncludeOperators name of its own. The reference-nav
        // form binds the existing two-type-parameter mock without a collection
        // ThenInclude overload. Dropping ThenInclude ends the walk.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"            return db.Orders.Include(o => o.Customer).ThenInclude(c => c.Orders).Where(o => o.Total > 0).{|LC042:ToList|}();"));
    }
}
