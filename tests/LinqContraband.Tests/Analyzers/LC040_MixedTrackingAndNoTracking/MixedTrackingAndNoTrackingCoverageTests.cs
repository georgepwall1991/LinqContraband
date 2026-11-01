using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking.MixedTrackingAndNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC040_MixedTrackingAndNoTracking;

/// <summary>
/// Leftover LC040 <c>MaterializerNames</c> the shipped fixtures do not isolate.
/// Dropping <c>ToArray</c> fails only that crime pin; the shipped <c>ToList</c>/<c>First</c>
/// fixtures stay green. The same holds for <c>ToHashSet</c>, <c>ToDictionary</c>,
/// <c>Last</c>, <c>AsEnumerable</c>, and the leftover async and single-row names.
/// <c>Count</c> is in <c>IsMaterializerMethod</c> but not <c>MaterializerNames</c>, so a
/// no-tracking <c>Count</c> next to a tracked <c>ToList</c> stays quiet. Try/catch path
/// modelling and <c>Select</c> short-circuit remain documented residuals and are not
/// locked as quiet.
/// </summary>
public partial class MixedTrackingAndNoTrackingTests
{
    private const string ExtraEfMaterializers = @"
namespace Microsoft.EntityFrameworkCore
{
    public static partial class EntityFrameworkQueryableExtensions
    {
        public static System.Collections.Generic.HashSet<TSource> ToHashSet<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<System.Collections.Generic.List<TSource>> ToListAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource[]> ToArrayAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> FirstAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> FirstOrDefaultAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> SingleAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> SingleOrDefaultAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> LastAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<TSource> LastOrDefaultAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
        public static System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<TKey, TSource>> ToDictionaryAsync<TSource, TKey>(this System.Linq.IQueryable<TSource> source, System.Func<TSource, TKey> keySelector) where TKey : notnull => null;
        public static System.Threading.Tasks.Task<System.Collections.Generic.HashSet<TSource>> ToHashSetAsync<TSource>(this System.Linq.IQueryable<TSource> source) => null;
    }
}
";

    private static string WrapCrime(string noTrackingCall, bool async = false)
    {
        var signature = async
            ? "async System.Threading.Tasks.Task Run(TestApp.AppDbContext db)"
            : "void Run(TestApp.AppDbContext db)";
        var awaitPrefix = async ? "await " : string.Empty;
        return EFCoreMock + ExtraEfMaterializers + Types + $@"
class Program
{{
    {signature}
    {{
        var first = db.Users.ToList();
        var second = {awaitPrefix}{{|LC040:db.Users.AsNoTracking().{noTrackingCall}|}};
    }}
}}";
    }

    [Fact]
    public async Task ToArray_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToArray()"));
    }

    [Fact]
    public async Task ToArrayAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToArrayAsync()", async: true));
    }

    [Fact]
    public async Task ToListAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToListAsync()", async: true));
    }

    [Fact]
    public async Task ToHashSet_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToHashSet()"));
    }

    [Fact]
    public async Task ToHashSetAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToHashSetAsync()", async: true));
    }

    [Fact]
    public async Task ToDictionary_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToDictionary(u => u.Id)"));
    }

    [Fact]
    public async Task ToDictionaryAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("ToDictionaryAsync(u => u.Id)", async: true));
    }

    [Fact]
    public async Task AsEnumerable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("AsEnumerable()"));
    }

    [Fact]
    public async Task FirstAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("FirstAsync()", async: true));
    }

    [Fact]
    public async Task FirstOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("FirstOrDefault()"));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("FirstOrDefaultAsync()", async: true));
    }

    [Fact]
    public async Task Single_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("Single()"));
    }

    [Fact]
    public async Task SingleAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("SingleAsync()", async: true));
    }

    [Fact]
    public async Task SingleOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("SingleOrDefault()"));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("SingleOrDefaultAsync()", async: true));
    }

    [Fact]
    public async Task Last_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("Last()"));
    }

    [Fact]
    public async Task LastAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("LastAsync()", async: true));
    }

    [Fact]
    public async Task LastOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("LastOrDefault()"));
    }

    [Fact]
    public async Task LastOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapCrime("LastOrDefaultAsync()", async: true));
    }

    [Fact]
    public async Task Count_StaysQuiet()
    {
        var test = EFCoreMock + ExtraEfMaterializers + Types + @"
class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.ToList();
        var n = db.Users.AsNoTracking().Count();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
