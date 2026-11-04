using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover <c>ImmediateQueryExecutionMethods</c> names the shipped
/// <c>ToList</c>/<c>Count</c>/<c>Any</c>/<c>First</c> fixtures and the draft
/// leftover execution-method file do not isolate. <c>FirstOrDefaultAsync</c>
/// already has helper-call crime cases. Dropping leftover <c>ToArray</c> fails
/// only that pin; the drain-loop <c>ToArray</c>/<c>ToArrayAsync</c> quiet
/// fixtures stay green. The same holds for leftover <c>FirstAsync</c>,
/// <c>Single</c>/<c>Last</c> families, leftover aggregates and leftover
/// <c>SumAsync</c>. A lookalike <c>LastAsync</c> on a proven EF receiver still
/// reports (name-only gate) so a later edit that requires an EF-declared
/// method is visible. In-memory <c>Single</c>/<c>Sum</c> stay quiet in the
/// shipped ignored-source fixtures.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string LeftoverTerminalMocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class LeftoverTerminalExtensions
    {
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) => Task.FromResult(new T[0]);
        public static Task<T> FirstAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate) => Task.FromResult(default(T));
        public static Task<T> SingleAsync<T>(this IQueryable<T> source) => Task.FromResult(default(T));
        public static Task<T> SingleOrDefaultAsync<T>(this IQueryable<T> source) => Task.FromResult(default(T));
        public static Task<T> LastAsync<T>(this IQueryable<T> source) => Task.FromResult(default(T));
        public static Task<T> LastOrDefaultAsync<T>(this IQueryable<T> source) => Task.FromResult(default(T));
        public static Task<int> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector) => Task.FromResult(0);
    }
}

namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static Task<T> LastAsync<T>(this IQueryable<T> source) => Task.FromResult(default(T));
    }
}
";

    private static string LeftoverTerminalProgram(string body) => LoopProgram(body) + LeftoverTerminalMocks;

    private static Task VerifyLeftoverTerminalAsync(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverTerminalProgram(body));

    [Fact]
    public Task ToArray_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var users = {|LC007:db.Users.Where(u => u.Id == id).ToArray()|};
        }");

    [Fact]
    public Task ToArrayAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var users = await {|LC007:db.Users.Where(u => u.Id == id).ToArrayAsync()|};
        }");

    [Fact]
    public Task FirstAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.FirstAsync(u => u.Id == id)|};
        }");

    [Fact]
    public Task Single_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = {|LC007:db.Users.Where(u => u.Id == id).Single()|};
        }");

    [Fact]
    public Task SingleOrDefault_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = {|LC007:db.Users.Where(u => u.Id == id).SingleOrDefault()|};
        }");

    [Fact]
    public Task SingleAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.Where(u => u.Id == id).SingleAsync()|};
        }");

    [Fact]
    public Task SingleOrDefaultAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.Where(u => u.Id == id).SingleOrDefaultAsync()|};
        }");

    [Fact]
    public Task Last_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = {|LC007:db.Users.Where(u => u.Id == id).Last()|};
        }");

    [Fact]
    public Task LastOrDefault_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = {|LC007:db.Users.Where(u => u.Id == id).LastOrDefault()|};
        }");

    [Fact]
    public Task LastAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.Where(u => u.Id == id).LastAsync()|};
        }");

    [Fact]
    public Task LastOrDefaultAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.Where(u => u.Id == id).LastOrDefaultAsync()|};
        }");

    [Fact]
    public Task Sum_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var total = {|LC007:db.Users.Where(u => u.ParentId == id).Sum(u => u.Id)|};
        }");

    [Fact]
    public Task Average_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var avg = {|LC007:db.Users.Where(u => u.ParentId == id).Average(u => u.Id)|};
        }");

    [Fact]
    public Task Min_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var min = {|LC007:db.Users.Where(u => u.ParentId == id).Min(u => u.Id)|};
        }");

    [Fact]
    public Task Max_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var max = {|LC007:db.Users.Where(u => u.ParentId == id).Max(u => u.Id)|};
        }");

    [Fact]
    public Task SumAsync_Reports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            var total = await {|LC007:db.Users.Where(u => u.ParentId == id).SumAsync(u => u.Id)|};
        }");

    [Fact]
    public Task LookalikeLastAsync_OnEfReceiver_StillReports() =>
        VerifyLeftoverTerminalAsync(@"foreach (var id in ids)
        {
            await {|LC007:OtherOrm.QueryExtensions.LastAsync(db.Users.Where(u => u.Id == id))|};
        }");
}
