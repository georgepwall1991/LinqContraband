using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover single-pass arms the 5.18.0 LC010 fixtures do not isolate on the
/// shared <c>LoopIterationExtensions</c> helper. Dropping <c>OptionUnsafe</c>
/// keeps the LanguageExt <c>Option</c> pin green and only fails that quiet pin.
/// A lookalike <c>OptionUnsafe</c> still reports. Dropping <c>yield break</c>
/// from <c>IsExitOf</c> fails only the branch <c>yield break</c> pin. Dropping
/// <c>goto</c> from <c>CanRestartOrJump</c> fails only the skip-<c>goto</c> pin.
/// </summary>
public partial class SaveChangesInLoopSinglePassTests
{
    private static string IteratorProgram(string body) => @"
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

class Program
{
    IEnumerable<int> Run(AppDbContext db, List<int> ids, int count)
    {
        " + body + @"
        yield return 0;
    }
}

class AppDbContext : DbContext { }

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public void Dispose() { }
    }
}
";

    [Fact]
    public Task OptionUnsafe_Foreach_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.OptionUnsafe<int> maybeUnsafe = default;
        foreach (var id in maybeUnsafe)
        {
            Console.WriteLine(id);
            await db.SaveChangesAsync();
        }"));

    [Fact]
    public Task LookalikeOptionUnsafe_Foreach_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"Lookalike.OptionUnsafe<int> otherUnsafe = default;
        foreach (var id in otherUnsafe)
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task YieldBreak_AfterSaveOnBranch_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(IteratorProgram(@"
        foreach (var id in ids)
        {
            if (id == count)
            {
                db.SaveChanges();
                yield break;
            }
        }"));

    [Fact]
    public Task Goto_SkippingReturn_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"foreach (var id in ids)
        {
            {|LC010:db.SaveChanges()|};
            if (id < 0)
                goto Skip;
            return null;
            Skip: ;
        }"));
}
