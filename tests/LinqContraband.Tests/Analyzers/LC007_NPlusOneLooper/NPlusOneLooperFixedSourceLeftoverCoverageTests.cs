using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover documented non-goals of <c>IsSequenceFixedInSource</c> that the 5.19.0
/// list-fixed-in-source pass did not probe: a C# 12 collection expression with a
/// target type (<c>foreach (var id in (int[])[1, 2, 3])</c>, a local whose only
/// assignment is one, a list collection expression, and a deconstruction foreach
/// over a collection expression of tuples) still reports. A spread of data
/// (<c>(int[])[..ids]</c>) still reports so a later edit that treats every
/// collection expression as fixed in source is visible. A bare
/// <c>foreach (var id in [1, 2, 3])</c> has no target type and does not compile.
/// Draft #740 owns <c>ImmutableArray.Create</c>, a <c>static readonly</c> array
/// field and the LC010 constant-bound <c>for</c> pins; this file does not restage them.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Fact]
    public Task LoopOverCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (int[])[1, 2, 3])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverALocalThatHoldsACollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"int[] wanted = [1, 2, 3];
        foreach (var id in wanted)
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverAListCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (List<int>)[1, 2, 3])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverACollectionExpressionOfTuples_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var (id, extra) in ((int, int)[])[(1, 0), (2, 0), (3, 0)])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id + extra)|};
        }"));

    [Fact]
    public Task LoopOverASpreadCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (int[])[..ids])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task PerElementLambdaOverCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(
            @"var users = ((int[])[1, 2, 3]).Select(id => {|LC007:db.Users.First(u => u.Id == id)|}).ToList();"));
}
