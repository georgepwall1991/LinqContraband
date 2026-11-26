using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover <c>ImmediateQueryExecutionMethods</c> and <c>SetBasedExecutorMethods</c>
/// names the shipped fixtures do not isolate. <c>ToList</c>, <c>ToListAsync</c>,
/// <c>Count</c>, <c>CountAsync</c>, <c>Any</c>, <c>AnyAsync</c>, <c>First</c>,
/// <c>FirstOrDefault</c> and <c>ExecuteDelete</c> already have crime cases.
/// Dropping leftover <c>ToHashSet</c> fails only that pin; the same holds for
/// <c>ToDictionary</c>, <c>ForEachAsync</c>, <c>LongCount</c>, <c>AllAsync</c>,
/// <c>MinAsync</c>/<c>MaxAsync</c>/<c>AverageAsync</c>, leftover async twins
/// and leftover <c>ExecuteUpdate</c>/<c>ExecuteUpdateAsync</c>. A lookalike
/// <c>ForEachAsync</c> on a proven EF receiver still reports (name-only gate)
/// so a later edit that requires an EF-declared method is visible. In-memory
/// <c>ToHashSet</c> over a chunk stays quiet in the shipped batch fixtures.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string ExtraExecutionMocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class LeftoverExecutionExtensions
    {
        public static Task ForEachAsync<T>(this IQueryable<T> source, Action<T> action) => Task.CompletedTask;
        public static Task<bool> AllAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate) => Task.FromResult(false);
        public static Task<int> MinAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector) => Task.FromResult(0);
        public static Task<int> MaxAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector) => Task.FromResult(0);
        public static Task<double> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector) => Task.FromResult(0.0);
        public static Task<HashSet<T>> ToHashSetAsync<T>(this IQueryable<T> source) => Task.FromResult(new HashSet<T>());
        public static Task<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> source, Func<T, TKey> keySelector) => Task.FromResult(new Dictionary<TKey, T>());
        public static Task<long> LongCountAsync<T>(this IQueryable<T> source) => Task.FromResult(0L);
    }
}

namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static Task ForEachAsync<T>(this IQueryable<T> source, Action<T> action) => Task.CompletedTask;
    }
}
";

    private static string LeftoverExecutionProgram(string body) => LoopProgram(body) + ExtraExecutionMocks;

    private static Task VerifyLeftoverExecutionAsync(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverExecutionProgram(body));

    [Fact]
    public Task ToHashSet_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var users = {|LC007:db.Users.Where(u => u.Id == id).ToHashSet()|};
        }");

    [Fact]
    public Task ToHashSetAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var users = await {|LC007:db.Users.Where(u => u.Id == id).ToHashSetAsync()|};
        }");

    [Fact]
    public Task ToDictionary_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var users = {|LC007:db.Users.Where(u => u.Id == id).ToDictionary(u => u.Id)|};
        }");

    [Fact]
    public Task ToDictionaryAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var users = await {|LC007:db.Users.Where(u => u.Id == id).ToDictionaryAsync(u => u.Id)|};
        }");

    [Fact]
    public Task ForEachAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            await {|LC007:db.Users.Where(u => u.Id == id).ForEachAsync(u => { })|};
        }");

    [Fact]
    public Task LongCount_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var total = {|LC007:db.Users.Where(u => u.Id == id).LongCount()|};
        }");

    [Fact]
    public Task LongCountAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var total = await {|LC007:db.Users.Where(u => u.Id == id).LongCountAsync()|};
        }");

    [Fact]
    public Task All_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var ok = {|LC007:db.Users.Where(u => u.ParentId == id).All(u => u.Id > 0)|};
        }");

    [Fact]
    public Task AllAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var ok = await {|LC007:db.Users.Where(u => u.ParentId == id).AllAsync(u => u.Id > 0)|};
        }");

    [Fact]
    public Task MinAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var min = await {|LC007:db.Users.Where(u => u.ParentId == id).MinAsync(u => u.Id)|};
        }");

    [Fact]
    public Task MaxAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var max = await {|LC007:db.Users.Where(u => u.ParentId == id).MaxAsync(u => u.Id)|};
        }");

    [Fact]
    public Task AverageAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            var avg = await {|LC007:db.Users.Where(u => u.ParentId == id).AverageAsync(u => u.Id)|};
        }");

    [Fact]
    public Task ExecuteUpdate_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            {|LC007:db.Users.Where(u => u.Id == id).ExecuteUpdate(x => new User { ParentId = 1 })|};
        }");

    [Fact]
    public Task ExecuteUpdateAsync_Reports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            await {|LC007:db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(x => new User { ParentId = 1 })|};
        }");

    [Fact]
    public Task LookalikeForEachAsync_OnEfReceiver_StillReports() =>
        VerifyLeftoverExecutionAsync(@"foreach (var id in ids)
        {
            await {|LC007:OtherOrm.QueryExtensions.ForEachAsync(db.Users.Where(u => u.Id == id), u => { })|};
        }");
}
