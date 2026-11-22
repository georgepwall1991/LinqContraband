using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover per-element operator names the shipped
/// <c>Select</c>/<c>Where</c>/<c>ForEach</c>/<c>ToDictionary</c>/<c>Sum</c>/
/// <c>OrderByDescending</c>/<c>ConvertAll</c>/<c>Exists</c>/<c>Parallel.ForEach</c>
/// fixtures do not isolate. Dropping one leftover name fails only that pin.
/// <c>ThenBy</c>/<c>ThenByDescending</c> keep the query in the leftover
/// lambda so <c>OrderBy</c> does not keep them quiet. Does not restage
/// leftover load-once wrap files or leftover execution-method files.
/// </summary>
public partial class NPlusOneLooperTests
{
    private Task LeftoverOperatorReports(string body, string query, string operatorName) =>
        VerifyCS.VerifyAnalyzerAsync(LambdaProgram(body), PerElementLambda(0, query, operatorName));

    private static async Task LeftoverOperatorReportsNet80(string body, string query, string operatorName)
    {
        var test = new CSharpAnalyzerTest<NPlusOneLooperAnalyzer, DefaultVerifier>
        {
            TestCode = LambdaProgram(body),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(PerElementLambda(0, query, operatorName));
        await test.RunAsync();
    }

    [Fact]
    public Task LeftoverSelectMany_Reports() =>
        LeftoverOperatorReports(
            "var nested = users.SelectMany(u => {|#0:db.Users.Where(x => x.ParentId == u.Id).ToList()|}).ToList();",
            "ToList",
            "SelectMany");

    [Fact]
    public Task LeftoverAny_Reports() =>
        LeftoverOperatorReports(
            "var any = users.Any(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "Any");

    [Fact]
    public Task LeftoverAll_Reports() =>
        LeftoverOperatorReports(
            "var all = users.All(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "All");

    [Fact]
    public Task LeftoverCount_Reports() =>
        LeftoverOperatorReports(
            "var n = users.Count(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "Count");

    [Fact]
    public Task LeftoverLongCount_Reports() =>
        LeftoverOperatorReports(
            "var n = users.LongCount(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "LongCount");

    [Fact]
    public Task LeftoverFirst_Reports() =>
        LeftoverOperatorReports(
            "var first = users.First(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "First");

    [Fact]
    public Task LeftoverFirstOrDefault_Reports() =>
        LeftoverOperatorReports(
            "var first = users.FirstOrDefault(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "FirstOrDefault");

    [Fact]
    public Task LeftoverLast_Reports() =>
        LeftoverOperatorReports(
            "var last = users.Last(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "Last");

    [Fact]
    public Task LeftoverLastOrDefault_Reports() =>
        LeftoverOperatorReports(
            "var last = users.LastOrDefault(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "LastOrDefault");

    [Fact]
    public Task LeftoverSingle_Reports() =>
        LeftoverOperatorReports(
            "var one = users.Single(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "Single");

    [Fact]
    public Task LeftoverSingleOrDefault_Reports() =>
        LeftoverOperatorReports(
            "var one = users.SingleOrDefault(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "SingleOrDefault");

    [Fact]
    public Task LeftoverMin_Reports() =>
        LeftoverOperatorReports(
            "var min = users.Min(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "Min");

    [Fact]
    public Task LeftoverMax_Reports() =>
        LeftoverOperatorReports(
            "var max = users.Max(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "Max");

    [Fact]
    public Task LeftoverAverage_Reports() =>
        LeftoverOperatorReports(
            "var avg = users.Average(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "Average");

    [Fact]
    public Task LeftoverMinBy_Reports() =>
        LeftoverOperatorReportsNet80(
            "var min = users.MinBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "MinBy");

    [Fact]
    public Task LeftoverMaxBy_Reports() =>
        LeftoverOperatorReportsNet80(
            "var max = users.MaxBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "MaxBy");

    [Fact]
    public Task LeftoverOrderBy_Reports() =>
        LeftoverOperatorReports(
            "var sorted = users.OrderBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();",
            "Count",
            "OrderBy");

    [Fact]
    public Task LeftoverThenBy_Reports() =>
        LeftoverOperatorReports(
            "var sorted = users.OrderBy(u => u.Id).ThenBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();",
            "Count",
            "ThenBy");

    [Fact]
    public Task LeftoverThenByDescending_Reports() =>
        LeftoverOperatorReports(
            "var sorted = users.OrderBy(u => u.Id).ThenByDescending(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();",
            "Count",
            "ThenByDescending");

    [Fact]
    public Task LeftoverGroupBy_Reports() =>
        LeftoverOperatorReports(
            "var groups = users.GroupBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();",
            "Count",
            "GroupBy");

    [Fact]
    public Task LeftoverToLookup_Reports() =>
        LeftoverOperatorReports(
            "var lookup = users.ToLookup(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});",
            "Count",
            "ToLookup");

    [Fact]
    public Task LeftoverDistinctBy_Reports() =>
        LeftoverOperatorReportsNet80(
            "var distinct = users.DistinctBy(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();",
            "Count",
            "DistinctBy");

    [Fact]
    public Task LeftoverTakeWhile_Reports() =>
        LeftoverOperatorReports(
            "var taken = users.TakeWhile(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|}).ToList();",
            "Any",
            "TakeWhile");

    [Fact]
    public Task LeftoverSkipWhile_Reports() =>
        LeftoverOperatorReports(
            "var skipped = users.SkipWhile(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|}).ToList();",
            "Any",
            "SkipWhile");

    [Fact]
    public Task LeftoverAggregate_Reports() =>
        LeftoverOperatorReports(
            "var total = ids.Aggregate(0, (sum, id) => sum + {|#0:db.Users.Count(x => x.ParentId == id)|});",
            "Count",
            "Aggregate");

    [Fact]
    public Task LeftoverZip_Reports() =>
        LeftoverOperatorReports(
            "var zipped = ids.Zip(users, (id, u) => {|#0:db.Users.Find(id)|}).ToList();",
            "Find",
            "Zip");

    [Fact]
    public Task LeftoverForAll_Reports() =>
        LeftoverOperatorReportsNet80(
            "ids.AsParallel().ForAll(id => { var user = {|#0:db.Users.Find(id)|}; });",
            "Find",
            "ForAll");

    [Fact]
    public Task LeftoverFind_Reports() =>
        LeftoverOperatorReports(
            "var found = users.Find(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "Find");

    [Fact]
    public Task LeftoverFindAll_Reports() =>
        LeftoverOperatorReports(
            "var found = users.FindAll(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "FindAll");

    [Fact]
    public Task LeftoverFindIndex_Reports() =>
        LeftoverOperatorReports(
            "var index = users.FindIndex(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "FindIndex");

    [Fact]
    public Task LeftoverFindLast_Reports() =>
        LeftoverOperatorReports(
            "var found = users.FindLast(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "FindLast");

    [Fact]
    public Task LeftoverFindLastIndex_Reports() =>
        LeftoverOperatorReports(
            "var index = users.FindLastIndex(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "FindLastIndex");

    [Fact]
    public Task LeftoverTrueForAll_Reports() =>
        LeftoverOperatorReports(
            "var all = users.TrueForAll(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "TrueForAll");

    [Fact]
    public Task LeftoverRemoveAll_Reports() =>
        LeftoverOperatorReports(
            "users.RemoveAll(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});",
            "Any",
            "RemoveAll");

    [Fact]
    public Task LeftoverParallelFor_Reports() =>
        LeftoverOperatorReports(
            "Parallel.For(0, ids.Count, i => { var user = {|#0:db.Users.Find(ids[i])|}; });",
            "Find",
            "For");

    [Fact]
    public Task LeftoverParallelForEachAsync_Reports() =>
        LeftoverOperatorReportsNet80(
            "await Parallel.ForEachAsync(ids, async (id, ct) => { _ = await {|#0:db.Users.FindAsync(id)|}; });",
            "FindAsync",
            "ForEachAsync");

    [Fact]
    public Task LeftoverLookalikeFindAll_OnUnrelatedCollection_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            LambdaProgram(
                "var hit = new LeftoverBag<User>().FindAll(u => db.Users.Any(x => x.ParentId == u.Id));",
                "    private sealed class LeftoverBag<T> { public T FindAll(Func<T, bool> match) => default; }"));
}
