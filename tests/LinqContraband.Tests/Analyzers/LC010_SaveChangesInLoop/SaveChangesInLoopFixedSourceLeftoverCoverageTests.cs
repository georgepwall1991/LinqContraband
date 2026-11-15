using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover documented non-goals of <c>IsSequenceFixedInSource</c> /
/// <c>IteratesItemsFixedInSource</c> that the 5.19.0 list-fixed-in-source pass
/// did not probe: a C# 12 collection expression with a target type
/// (<c>foreach (var id in (int[])[1, 2, 3])</c>) still reports. A spread of
/// data (<c>(int[])[..ids]</c>) still reports so a later edit that treats every
/// collection expression as fixed in source is visible. A bare
/// <c>foreach (var id in [1, 2, 3])</c> has no target type and does not compile.
/// Draft #740 owns <c>ImmutableArray.Create</c>, a <c>static readonly</c> array
/// field and the constant-bound <c>for</c> pins; this file does not restage them
/// or edit the shared LC010 single-pass fixture.
/// </summary>
public class SaveChangesInLoopFixedSourceLeftoverCoverageTests
{
    private static string Program(string body) => @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    async Task<string> Run(AppDbContext db, int[] ids)
    {
        " + body + @"
        return null;
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
    public Task SaveInALoopOverCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in (int[])[1, 2, 3])
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverALocalThatHoldsACollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"int[] wanted = [1, 2, 3];
        foreach (var id in wanted)
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverACollectionExpressionOfTuples_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var (id, extra) in ((int, int)[])[(1, 0), (2, 0), (3, 0)])
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task SaveInALoopOverASpreadCollectionExpression_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in (int[])[..ids])
        {
            {|LC010:db.SaveChanges()|};
        }"));
}
