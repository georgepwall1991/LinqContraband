using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover documented non-goals of <c>IsSequenceFixedInSource</c> /
/// <c>IteratesItemsFixedInSource</c> that the 5.19.0 list-fixed-in-source pass
/// and draft #742's array/list collection-expression pins did not probe: a C# 12
/// collection expression targeting <c>ReadOnlySpan&lt;T&gt;</c> or
/// <c>Span&lt;T&gt;</c>, a local whose only assignment is a span collection
/// expression, and a <c>stackalloc</c> array with an initializer still report. A
/// spread of data (<c>(ReadOnlySpan&lt;int&gt;)[..ids]</c>) and
/// <c>stackalloc int[count]</c> still report so a later edit that treats every
/// span-backed collection expression or every <c>stackalloc</c> as fixed in source
/// is visible. Draft #742 owns array/list collection-expression pins on an async
/// fixture; this file uses a sync method because a ref struct cannot live in an
/// async method, and does not restage those method names or edit the shared LC010
/// single-pass fixture.
/// </summary>
public class SaveChangesInLoopFixedSourceSpanLeftoverCoverageTests
{
    private static string Program(string body) => @"
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    void Run(AppDbContext db, int[] ids, int count)
    {
        " + body + @"
    }
}

class AppDbContext : DbContext { }

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Dispose() { }
    }
}
";

    [Fact]
    public Task SaveInALoopOverReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in (ReadOnlySpan<int>)[1, 2, 3])
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverSpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in (Span<int>)[1, 2, 3])
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverALocalThatHoldsAReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"ReadOnlySpan<int> wanted = [1, 2, 3];
        foreach (var id in wanted)
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverStackallocArray_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in stackalloc int[] { 1, 2, 3 })
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverStackallocSizedByData_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in stackalloc int[count])
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverASpreadReadOnlySpanCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in (ReadOnlySpan<int>)[..ids])
        {
            {|LC010:db.SaveChanges()|};
        }"));
}
