using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A loop over a list that is written out in the source (<c>new[] { "Admin", "User" }</c>) or over the values of
/// an enum runs a number of queries the code fixes, not one per row of data. That is not an N+1, so LC007 stays
/// quiet on it and looks at the next loop out. A sequence whose length the data decides still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static string FixedSourceProgram(string body) => Usings + @"
enum Status { Draft, Paid, Shipped }

class Wanted : System.Collections.Generic.IEnumerable<int>
{
    public int[] Items { get; set; }
    public void Add(int item) { }
    public System.Collections.Generic.IEnumerator<int> GetEnumerator() => null;
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
}

class Program
{
    void Run(MyDbContext db, int[] ids, int first, int second, int count)
    {
        " + body + @"
    }
}
" + MockNamespace;

    [Theory]
    [InlineData("new[] { 1, 2, 3 }")]
    [InlineData("new int[] { 1, 2, 3 }")]
    // The items are data, but there are still exactly two of them.
    [InlineData("new[] { first, second }")]
    [InlineData("new List<int> { 1, 2, 3 }")]
    [InlineData("new HashSet<int> { first, second }")]
    [InlineData("new Wanted { 1, 2 }")]
    public Task LoopOverAListWrittenInTheSource_IsQuiet(string source) =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in " + source + @")
        {
            var found = db.Users.Count(u => u.Id == id);
        }"));

    [Theory]
    [InlineData("Enum.GetValues(typeof(Status)).Cast<Status>()")]
    [InlineData("(Status[])Enum.GetValues(typeof(Status))")]
    [InlineData("Enum.GetValues(typeof(Status)).OfType<Status>()")]
    public Task LoopOverEnumValues_IsQuiet(string source) =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"var counts = new Dictionary<Status, int>();
        foreach (var status in " + source + @")
        {
            counts[status] = db.Users.Count(u => u.Id == (int)status);
        }"));

    [Fact]
    public Task LoopOverEnumNames_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var name in Enum.GetNames(typeof(Status)))
        {
            var found = db.Users.Count(u => u.Id == name.Length);
        }"));

    [Theory]
    // A local that only ever holds the list.
    [InlineData(@"var wanted = new[] { 1, 2, 3 };
        foreach (var id in wanted)
        {
            var found = db.Users.Count(u => u.Id == id);
        }")]
    [InlineData(@"var statuses = (Status[])Enum.GetValues(typeof(Status));
        foreach (var status in statuses)
        {
            var found = db.Users.Count(u => u.Id == (int)status);
        }")]
    public Task LoopOverALocalThatHoldsTheList_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(body));

    [Fact]
    public Task PerElementLambdaOverAListWrittenInTheSource_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(
            @"var users = new[] { 1, 2, 3 }.Select(id => db.Users.First(u => u.Id == id)).ToList();"));

    [Theory]
    // Sized by data.
    [InlineData("new int[count]")]
    [InlineData("new[] { 1, 2 }.Concat(ids)")]
    [InlineData("new List<int>(ids) { 0 }")]
    [InlineData("new Wanted { Items = ids }")]
    [InlineData("Enumerable.Range(0, count)")]
    [InlineData("ids")]
    public Task LoopOverASequenceSizedByData_StillReports(string source) =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in " + source + @")
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Theory]
    // The local is assigned more than once, or grows after it is declared.
    [InlineData(@"var wanted = new[] { 1, 2, 3 };
        if (count > 3) wanted = ids;
        foreach (var id in wanted)
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }")]
    [InlineData(@"var wanted = new List<int> { 1, 2, 3 };
        wanted.AddRange(ids);
        foreach (var id in wanted)
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }")]
    public Task LoopOverALocalThatMayHoldData_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(body));

    [Fact]
    public Task FixedListInsideAPerItemLoop_ReportsForTheOuterLoop() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in ids)
        {
            foreach (var offset in new[] { 0, 1 })
            {
                var found = {|LC007:db.Users.Count(u => u.Id == id + offset)|};
            }
        }"));

    [Fact]
    public Task PerElementLambdaOverData_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(
            @"var users = ids.Select(id => {|LC007:db.Users.First(u => u.Id == id)|}).ToList();"));
}
