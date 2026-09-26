using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow.TransactionScopeWithoutAsyncFlowAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC058_TransactionScopeWithoutAsyncFlow;

/// <summary>
/// Leftover 5.15.0 arms the original 22 analyzer cases do not isolate.
/// Dropping <c>IUsingOperation.IsAsynchronous</c> keeps <c>await</c> and
/// <c>await foreach</c> green and only fails the await-using statement pin.
/// Dropping <c>IUsingDeclarationOperation.IsAsynchronous</c> keeps the
/// statement pin green and only fails the await-using declaration pin.
/// Replacing the parenthesize unwrap loop with a single unwrap keeps one-level
/// parens on a using declaration green and only fails the doubly-parenthesized
/// expression pin. An <c>await using</c> after the scope ends stays quiet so a
/// later edit that treats any await-using in the method as in-scope is visible.
/// A using declaration directly in a switch section is CS8647 and is not pinned.
/// </summary>
public partial class TransactionScopeWithoutAsyncFlowTests
{
    [Fact]
    public async Task AwaitUsingStatement_InsideScope_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            using (var scope = {|#0:new TransactionScope()|})
            {
                await using (var stream = new System.IO.MemoryStream())
                {
                }
            }"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task AwaitUsingDeclaration_InsideScope_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            using (var scope = {|#0:new TransactionScope()|})
            {
                await using var stream = new System.IO.MemoryStream();
            }"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task DoublyParenthesizedCreation_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            using ((({|#0:new TransactionScope()|})))
            {
                await db.SaveChangesAsync(ct);
            }"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task ParenthesizedUsingDeclaration_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            using var scope = ({|#0:new TransactionScope()|});
            await db.SaveChangesAsync(ct);
            scope.Complete();"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task AwaitUsing_AfterScopeEnds_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            using (var scope = new TransactionScope())
            {
                db.SaveChanges();
                scope.Complete();
            }
            await using var stream = new System.IO.MemoryStream();"));
    }
}
