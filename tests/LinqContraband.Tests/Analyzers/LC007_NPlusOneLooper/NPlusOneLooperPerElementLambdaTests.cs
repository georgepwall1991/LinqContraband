using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A lambda that an in-memory operator runs once per element is a loop written as an expression:
/// <c>ids.Select(id => db.Users.Find(id))</c>, <c>users.ForEach(...)</c>, <c>Parallel.ForEach(...)</c>. LC007
/// reports a database execution inside one, and stays quiet on expression-tree lambdas (which the database runs
/// as one query), on lambdas that are not per element (cache factories, <c>Task.Run</c>), and on <c>Chunk</c> batches.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static string LambdaProgram(string body, string members = "") => Usings + @"
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

class Program
{
    private readonly MyDbContext db = new MyDbContext();
" + members + @"
    async Task Run(List<int> ids, List<User> users, int[] idArray)
    {
" + body + @"
        await Task.CompletedTask;
    }
}
" + MockNamespace + HelperMocks;

    private static DiagnosticResult PerElementLambda(int location, string query, string operatorName) =>
        VerifyCS.Diagnostic(NPlusOneLooperAnalyzer.PerElementLambdaRule).WithLocation(location).WithArguments(query, operatorName);

    private static DiagnosticResult PerElementLambdaHelperCall(int location, string helper, string query, string operatorName) =>
        VerifyCS.Diagnostic(NPlusOneLooperAnalyzer.PerElementLambdaHelperCallRule).WithLocation(location).WithArguments(helper, query, operatorName);

    [Theory]
    [InlineData("var found = ids.Select(id => {|#0:db.Users.Find(id)|}).ToList();", "Find", "Select")]
    [InlineData("users.ForEach(u => { var n = {|#0:db.Users.Count(x => x.ParentId == u.Id)|}; });", "Count", "ForEach")]
    [InlineData("var parents = users.Where(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|}).ToList();", "Any", "Where")]
    [InlineData("var counts = users.ToDictionary(u => u.Id, u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});", "Count", "ToDictionary")]
    [InlineData("var total = users.Sum(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|});", "Count", "Sum")]
    [InlineData("var sorted = users.OrderByDescending(u => {|#0:db.Users.Count(x => x.ParentId == u.Id)|}).ToList();", "Count", "OrderByDescending")]
    [InlineData("var loaded = await Task.WhenAll(ids.Select(id => {|#0:db.Users.FindAsync(id)|}));", "FindAsync", "Select")]
    [InlineData("var loaded = await Task.WhenAll(ids.Select(async id => await {|#0:db.Users.FindAsync(id)|}));", "FindAsync", "Select")]
    [InlineData("Parallel.ForEach(ids, id => { var user = {|#0:db.Users.Find(id)|}; });", "Find", "ForEach")]
    [InlineData("var names = Array.ConvertAll(idArray, id => {|#0:db.Users.Find(id)|});", "Find", "ConvertAll")]
    [InlineData("var any = users.Exists(u => {|#0:db.Users.Any(x => x.ParentId == u.Id)|});", "Any", "Exists")]
    [InlineData("var joined = string.Join(\",\", ids.Select(id => {|#0:db.Users.Where(u => u.Id == id).ToList()|}.Count));", "ToList", "Select")]
    [InlineData(@"var counts = (from u in users
                      let children = {|#0:db.Users.Count(x => x.ParentId == u.Id)|}
                      select children).ToList();", "Count", "Select")]
    public Task PerElementLambda_RunningQuery_Reports(string body, string query, string operatorName) =>
        VerifyCS.VerifyAnalyzerAsync(LambdaProgram(body), PerElementLambda(0, query, operatorName));

    [Fact]
    public Task PerElementLambda_CallingHelperThatRunsQuery_ReportsCall() =>
        VerifyCS.VerifyAnalyzerAsync(
            LambdaProgram(
                "var found = ids.Select(id => {|#0:Load(id)|}).ToList();",
                "    private User Load(int id) => db.Users.Find(id);"),
            PerElementLambdaHelperCall(0, "Load", "Find", "Select"));

    [Theory]
    // An expression-tree lambda is translated into the one query that contains it.
    [InlineData("var parents = db.Users.Where(u => db.Users.Any(x => x.ParentId == u.Id)).ToList();")]
    // A delegate lambda nested inside an expression tree is still part of that one translated query.
    [InlineData("var matched = db.Users.Where(u => ids.Any(id => db.Users.Count(x => x.ParentId == id) > u.Id)).ToList();")]
    [InlineData("var shaped = db.Users.Select(u => new { u.Id, Hits = ids.Count(id => db.Users.Any(x => x.Id == id && x.ParentId == u.Id)) }).ToList();")]
    // Not per element: these delegates run once, or when the callee decides.
    [InlineData("var user = await Task.Run(() => db.Users.Find(1));")]
    [InlineData("var lazy = new Lazy<User>(() => db.Users.Find(1));")]
    [InlineData("Func<int, User> load = id => db.Users.Find(id);")]
    [InlineData("var cached = await cache.GetOrCreateAsync(\"k\", _ => db.Users.FirstOrDefaultAsync(u => u.Id == 1));")]
    // A cache factory inside a per-element lambda is still a cache factory.
    [InlineData("var cached = ids.Select(id => cache.GetOrCreateAsync(id, _ => db.Users.FirstOrDefaultAsync(u => u.Id == id))).ToList();")]
    // Only the accumulator of Aggregate runs per element; the result selector runs once.
    [InlineData("var total = ids.Aggregate(0, (sum, id) => sum + id, sum => db.Users.Count());")]
    // In-memory work per element.
    [InlineData("var counts = users.Select(u => users.Count(x => x.ParentId == u.Id)).ToList();")]
    // An operator that merely shares a name with a LINQ one.
    [InlineData("var mapped = new Maybe<int>().Select(id => db.Users.Find(id));")]
    public Task Lambda_NotPerElementOrNotEf_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(
            LambdaProgram(
                body,
                @"    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache cache = null;
    private sealed class Maybe<T> { public Maybe<TResult> Select<TResult>(Func<T, TResult> map) => new Maybe<TResult>(); }"));

    [Theory]
    [InlineData("var users2 = ids.Chunk(500).Select(batch => db.Users.Where(u => batch.Contains(u.Id)).ToList()).ToList();")]
    [InlineData("ids.Chunk(500).ToList().ForEach(batch => { var page = db.Users.Where(u => batch.Contains(u.Id)).ToList(); });")]
    public async Task PerElementLambda_OverChunkBatches_IsQuiet(string body)
    {
        var test = new CSharpAnalyzerTest<NPlusOneLooperAnalyzer, DefaultVerifier>
        {
            TestCode = LambdaProgram(body),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task PerElementLambda_ExplicitLoad_HasNoCodeFix()
    {
        var test = LambdaProgram("users.ForEach(u => {|#0:db.Entry(u).Collection(x => x.Orders).Load()|});");

        await Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<NPlusOneLooperAnalyzer, NPlusOneLooperFixer>
            .VerifyCodeFixAsync(test, PerElementLambda(0, "Load", "ForEach"), test);
    }
}
