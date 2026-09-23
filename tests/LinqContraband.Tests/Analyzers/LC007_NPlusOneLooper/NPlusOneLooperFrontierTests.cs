using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A hierarchy walked one level per query (Jellyfin's descendant resolution): the loop runs while a frontier of
/// ids is non-empty, each query reads the whole frontier (<c>frontier.Contains(...)</c> or the frontier passed to
/// a helper), and the frontier is refilled from the query result. That is one query per level, not one per item,
/// so it stays quiet. A worklist that takes one item at a time (<c>Dequeue</c>, <c>Pop</c>, <c>frontier[0]</c>)
/// is still one query per item and still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Theory]
    // Level by level, refilled through foreach + Add with a visited set.
    [InlineData(@"var frontier = new List<int> { 1 };
        var visited = new HashSet<int>(frontier);
        while (frontier.Count != 0)
        {
            var next = db.Users.Where(u => frontier.Contains(u.ParentId)).Select(u => u.Id).ToList();
            frontier = new List<int>();
            foreach (var id in next)
            {
                if (visited.Add(id)) frontier.Add(id);
            }
        }")]
    // The query is built in loop locals first, and the frontier refilled with AddRange.
    [InlineData(@"var frontier = new List<int> { 1 };
        while (frontier.Count > 0)
        {
            var children = db.Users.Where(u => frontier.Contains(u.ParentId)).Select(u => u.Id);
            var next = children.Distinct().ToArray();
            frontier = new List<int>();
            frontier.AddRange(next);
        }")]
    // The frontier is a set passed to Enumerable.Contains, and the condition asks Any().
    [InlineData(@"var frontier = new HashSet<int> { 1 };
        while (frontier.Any())
        {
            var next = db.Users.Where(u => Enumerable.Contains(frontier, u.ParentId)).Select(u => u.Id).ToList();
            frontier.Clear();
            foreach (var id in next) frontier.Add(id);
        }")]
    // The result replaces the frontier directly.
    [InlineData(@"var frontier = new List<int> { 1 };
        while (frontier.Count > 0)
        {
            frontier = await db.Users.Where(u => frontier.Contains(u.ParentId)).Select(u => u.Id).ToListAsync();
        }")]
    public Task LevelByLevelFrontierLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(FrontierProgram(body));

    [Theory]
    // A queue walked one node at a time: one query per node.
    [InlineData(@"var pending = new Queue<int>();
        pending.Enqueue(1);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            var children = {|LC007:db.Users.Where(u => u.ParentId == id).Select(u => u.Id).ToList()|};
            foreach (var child in children) pending.Enqueue(child);
        }")]
    [InlineData(@"var pending = new Stack<int>();
        pending.Push(1);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            var children = {|LC007:db.Users.Where(u => u.ParentId == id).Select(u => u.Id).ToList()|};
            foreach (var child in children) pending.Push(child);
        }")]
    [InlineData(@"var frontier = new List<int> { 1 };
        while (frontier.Count > 0)
        {
            var id = frontier[0];
            frontier.RemoveAt(0);
            var children = {|LC007:db.Users.Where(u => u.ParentId == id).Select(u => u.Id).ToList()|};
            frontier.AddRange(children);
        }")]
    // The frontier is read as a whole, but it is never refilled from the result.
    [InlineData(@"var frontier = new List<int>(ids);
        while (frontier.Count > 0)
        {
            var users = {|LC007:db.Users.Where(u => frontier.Contains(u.Id)).ToList()|};
            frontier.RemoveAt(0);
        }")]
    public Task OneItemAtATimeWorklist_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(FrontierProgram(body));

    private static string FrontierProgram(string body) => Usings + @"
class Program
{
    async Task Run(MyDbContext db, int[] ids)
    {
        " + body + @"
    }
}
" + MockNamespace;
}
