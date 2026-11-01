using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC062_BlockingEfAsyncCall.BlockingEfAsyncCallAnalyzer>;

using LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

namespace LinqContraband.Tests.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Leftover 5.16.0 LC062 arms the original fixtures do not isolate.
/// <c>TryGetSingleUnobservedAssignment</c> treats an <c>await foreach</c>, <c>await using</c>
/// statement or <c>await using</c> declaration between the assignment and the blocking site
/// as a possible completion (the shipped cases only <c>await</c> the task or
/// <c>Task.Delay</c>). <c>IsInsideNameOf</c> keeps <c>nameof(task.Result)</c> quiet.
/// <c>Wait(CancellationToken)</c> has no timeout argument, so it is not a zero-timeout poll,
/// and the fixer withholds an await rewrite (it only rewrites parameterless <c>Wait()</c>).
/// Field-held tasks and <c>nameof</c> followed by a later <c>.Result</c> are not pinned.
/// </summary>
public class BlockingEfAsyncCallCoverageTests
{
    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult Reported() =>
        new Microsoft.CodeAnalysis.Testing.DiagnosticResult(
            BlockingEfAsyncCallAnalyzer.DiagnosticId,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).WithLocation(0);

    [Fact]
    public async Task AwaitForeach_BetweenAssignmentAndResult_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BlockingEfAsyncCallTests.Wrap(@"
        async System.Collections.Generic.IAsyncEnumerable<int> Empty() { yield break; }
        var task = db.Users.ToListAsync();
        await foreach (var n in Empty()) { }
        var users = task.Result;", isAsync: true));
    }

    [Fact]
    public async Task AwaitUsingStatement_BetweenAssignmentAndResult_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BlockingEfAsyncCallTests.Wrap(@"
        var task = db.Users.ToListAsync();
        await using (var stream = new System.IO.MemoryStream()) { }
        var users = task.Result;", isAsync: true));
    }

    [Fact]
    public async Task AwaitUsingDeclaration_BetweenAssignmentAndResult_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BlockingEfAsyncCallTests.Wrap(@"
        var task = db.Users.ToListAsync();
        await using var stream = new System.IO.MemoryStream();
        var users = task.Result;", isAsync: true));
    }

    [Fact]
    public async Task Nameof_TaskResult_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BlockingEfAsyncCallTests.Wrap(@"
        var task = db.Users.ToListAsync();
        var name = nameof(task.Result);"));
    }

    [Fact]
    public async Task Wait_WithCancellationToken_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            BlockingEfAsyncCallTests.Wrap(@"{|#0:db.SaveChangesAsync().Wait(ct)|};"),
            Reported());
    }
}
