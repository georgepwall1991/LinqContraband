using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Leftover LC017 <c>CollectionMutators</c> names the shipped <c>Add</c>/<c>Clear</c>
/// write-path fixtures do not isolate. Dropping leftover <c>AddRange</c> FNs only
/// <see cref="AddRange_StaysQuiet"/>; the shipped <c>Tags.Add</c> fixture stays green.
/// The same holds for leftover <c>Remove</c>, <c>RemoveAll</c>, <c>RemoveAt</c>,
/// <c>RemoveRange</c>, <c>RemoveWhere</c>, <c>Insert</c> and <c>InsertRange</c>.
/// <c>RemoveAll</c>/<c>RemoveWhere</c> predicates must not capture the entity local:
/// a lambda that reads <c>e</c> stays quiet through the lambda-escape arm instead.
/// A leftover name on an unrelated collection still reports, so a later edit that
/// treats every matching name as a tracked-graph mutation is visible.
/// </summary>
public partial class WholeEntityProjectionTests
{
    private static Task VerifyLeftoverMutatorQuietAsync(string mutation) =>
        VerifyCS.VerifyAnalyzerAsync(WriteProgram(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            " + mutation + @"
        db.SaveChanges();"));

    [Fact]
    public Task AddRange_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.AddRange(new[] { new Tag { Label = e.Name } });");

    [Fact]
    public Task Remove_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.Remove(new Tag { Label = e.Name });");

    [Fact]
    public Task RemoveAll_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.RemoveAll(t => t.Label == \"x\");");

    [Fact]
    public Task RemoveAt_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.RemoveAt(0);");

    [Fact]
    public Task RemoveRange_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.RemoveRange(0, 1);");

    [Fact]
    public Task RemoveWhere_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.UniqueTags.RemoveWhere(t => t.Label == \"x\");");

    [Fact]
    public Task Insert_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.Insert(0, new Tag { Label = e.Name });");

    [Fact]
    public Task InsertRange_StaysQuiet() =>
        VerifyLeftoverMutatorQuietAsync("e.Tags.InsertRange(0, new[] { new Tag { Label = e.Name } });");

    [Fact]
    public Task LookalikeAddRange_OnUnrelatedCollection_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(WriteProgram(@"var entities = {|LC017:_riches.Where(e => e.Id > 0).ToList()|};
        var others = new List<Tag>();
        foreach (var e in entities)
            others.AddRange(new[] { new Tag { Label = e.Name } });"));
}
