using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC014_AvoidStringCaseConversion.AvoidStringCaseConversionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC014_AvoidStringCaseConversion;

/// <summary>
/// Leftover LC014 names the original 28 fixtures do not isolate.
/// <c>ToLower</c>, <c>ToUpper</c> and <c>ToLowerInvariant</c> already have crime
/// cases. <c>ToUpperInvariant</c> is listed beside them, but no fixture marks
/// <c>{|LC014|}</c> on it. The shipped <c>Where</c>, <c>OrderBy</c>, <c>AnyAsync</c>
/// and <c>Join</c> fixtures leave the rest of <c>TargetLinqMethods</c> and
/// <c>TargetEfAsyncPredicateMethods</c> unpinned. Extra EF async stubs live in
/// this file only. Dropping <c>ToUpperInvariant</c> fails only that pin; dropping
/// a leftover operator name fails only that operator's pin. <c>db.Set&lt;T&gt;()</c>
/// is already caught by <c>Type.IsDbSet()</c> and is not isolatable. Query-syntax
/// <c>orderby</c> stays unpinned.
/// </summary>
public partial class AvoidStringCaseConversionTests
{
    private const string LeftoverEfAsyncMocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public static partial class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<int> CountAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => null;

        public static System.Threading.Tasks.Task<long> LongCountAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => null;

        public static System.Threading.Tasks.Task<bool> AllAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => null;

        public static System.Threading.Tasks.Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;

        public static System.Threading.Tasks.Task<TSource> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;

        public static System.Threading.Tasks.Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;

        public static System.Threading.Tasks.Task<TSource> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;

        public static System.Threading.Tasks.Task<TSource> LastAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;

        public static System.Threading.Tasks.Task<TSource> LastOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => default;
    }
}
";

    private static string Wrap(string body, bool async = false)
    {
        var signature = async
            ? "public async System.Threading.Tasks.Task TestMethod()"
            : "public void TestMethod()";

        return Usings + TestClasses + LeftoverEfAsyncMocks + $@"
namespace LinqContraband.Test
{{
    public class TestClass
    {{
        {signature}
        {{
            using var db = new AppDbContext();
            {body}
        }}
    }}
}}";
    }

    [Fact]
    public async Task ToUpperInvariant_InWhere_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Users.Where(u => {|LC014:u.Name.ToUpperInvariant()|} == ""TEST"");"));
    }

    [Fact]
    public async Task OrderByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Users.OrderByDescending(u => {|LC014:u.Name.ToLower()|});"));
    }

    [Fact]
    public async Task ThenBy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Users.OrderBy(u => u.Age).ThenBy(u => {|LC014:u.Name.ToLower()|});"));
    }

    [Fact]
    public async Task ThenByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var result = db.Users.OrderBy(u => u.Age).ThenByDescending(u => {|LC014:u.Name.ToLower()|});"));
    }

    [Fact]
    public async Task Count_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var n = db.Users.Count(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task LongCount_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var n = db.Users.LongCount(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task Any_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var exists = db.Users.Any(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task All_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var all = db.Users.All(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task First_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.First(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task FirstOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.FirstOrDefault(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task Single_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.Single(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task SingleOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.SingleOrDefault(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task Last_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.Last(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task LastOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = db.Users.LastOrDefault(u => {|LC014:u.Name.ToLower()|} == ""test"");"));
    }

    [Fact]
    public async Task GroupJoin_OuterEfKeySelector_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var aliases = new List<User>();
            var result = db.Users.GroupJoin(
                aliases,
                u => {|LC014:u.Name.ToLower()|},
                alias => alias.Name,
                (u, matches) => u);"));
    }

    [Fact]
    public async Task GroupJoin_InMemoryInnerKeySelector_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var aliases = new List<User>();
            var result = db.Users.GroupJoin(
                aliases,
                u => u.Name,
                alias => alias.Name.ToLower(),
                (u, matches) => u);"));
    }

    [Fact]
    public async Task CountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var n = await db.Users.CountAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task LongCountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var n = await db.Users.LongCountAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task AllAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var all = await db.Users.AllAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task FirstAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.FirstAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.FirstOrDefaultAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task SingleAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.SingleAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.SingleOrDefaultAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task LastAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.LastAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }

    [Fact]
    public async Task LastOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var user = await db.Users.LastOrDefaultAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");", async: true));
    }
}
