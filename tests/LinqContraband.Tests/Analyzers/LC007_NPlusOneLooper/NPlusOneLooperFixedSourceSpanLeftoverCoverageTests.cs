using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover documented non-goals of <c>IsSequenceFixedInSource</c> that the 5.19.0
/// list-fixed-in-source pass and draft #742's array/list collection-expression pins
/// did not probe: a C# 12 collection expression targeting <c>ReadOnlySpan&lt;T&gt;</c>
/// or <c>Span&lt;T&gt;</c>, a local whose only assignment is a span collection
/// expression, and a <c>stackalloc</c> array with an initializer still report. A
/// spread of data (<c>(ReadOnlySpan&lt;int&gt;)[..ids]</c>) and
/// <c>stackalloc int[count]</c> still report so a later edit that treats every
/// span-backed collection expression or every <c>stackalloc</c> as fixed in source
/// is visible. Draft #742 owns array/list collection-expression pins; this file
/// does not restage them. Span pins use the shared sync <c>FixedSourceProgram</c>
/// fixture because a ref struct cannot live in an async method.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Fact]
    public Task LoopOverReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (ReadOnlySpan<int>)[1, 2, 3])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverSpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (Span<int>)[1, 2, 3])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverALocalThatHoldsAReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"ReadOnlySpan<int> wanted = [1, 2, 3];
        foreach (var id in wanted)
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverStackallocArray_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in stackalloc int[] { 1, 2, 3 })
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverStackallocSizedByData_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in stackalloc int[count])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));

    [Fact]
    public Task LoopOverASpreadReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(FixedSourceProgram(@"foreach (var id in (ReadOnlySpan<int>)[..ids])
        {
            var found = {|LC007:db.Users.Count(u => u.Id == id)|};
        }"));
}
