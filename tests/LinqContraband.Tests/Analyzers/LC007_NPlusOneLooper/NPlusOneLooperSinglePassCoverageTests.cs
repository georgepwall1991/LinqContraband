using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover single-pass arms the 5.18.0 fixtures do not isolate.
/// Dropping <c>OptionUnsafe</c> keeps the LanguageExt <c>Option</c> pin green and
/// only fails the <c>OptionUnsafe</c> quiet pin. A lookalike <c>OptionUnsafe</c>
/// still reports so a later edit that treats every matching type name as
/// LanguageExt is visible. Dropping <c>yield break</c> from <c>IsExitOf</c> keeps
/// the always-exiting <c>return</c>/<c>break</c> pins green and only fails the
/// branch <c>yield break</c> pin. Dropping <c>ForStatementSyntax</c> or
/// <c>DoStatementSyntax</c> from <c>LeavesLoopAfter</c> fails only that loop
/// shape's quiet pin. Dropping <c>goto</c> from <c>CanRestartOrJump</c> keeps the
/// <c>continue</c>-before-exit pin green and only fails the skip-<c>goto</c> pin.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static string IteratorProgram(string body) => Usings + @"
class Program
{
    System.Collections.Generic.IEnumerable<int> Run(MyDbContext db, int[] ids, int count)
    {
        " + body + @"
        yield return 0;
    }

    static void Handle(List<User> users) { }
}
" + MockNamespace;

    [Fact]
    public Task OptionUnsafe_Foreach_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.OptionUnsafe<int> maybeId = default;
        foreach (var id in maybeId)
        {
            var users = await db.Users.Where(u => u.ParentId == id).ToListAsync();
            Handle(users);
        }"));

    [Fact]
    public Task LookalikeOptionUnsafe_Foreach_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"Lookalike.OptionUnsafe<int> maybeId = default;
        foreach (var id in maybeId)
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }"));

    [Fact]
    public Task YieldBreak_AfterQueryOnBranch_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(IteratorProgram(@"
        foreach (var id in ids)
        {
            if (id == count)
            {
                var user = db.Users.Find(id);
                System.Console.WriteLine(user.Id);
                yield break;
            }
        }"));

    [Fact]
    public Task Goto_SkippingReturn_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.FindAsync(id)|};
            if (user == null)
                goto Skip;
            return;
            Skip: ;
        }"));

    [Fact]
    public Task ForLoop_QueryThenReturnOnBranch_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"for (var i = 0; i < count; i++)
        {
            if (i == 0)
            {
                var user = await db.Users.FindAsync(ids[i]);
                return;
            }
        }"));

    [Fact]
    public Task DoLoop_QueryThenReturnOnBranch_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"do
        {
            if (count == 3)
            {
                var user = await db.Users.FindAsync(ids[0]);
                return;
            }
        } while (count > 0);"));
}
