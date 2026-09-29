using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC059_DisposedContextConnection.DisposedContextConnectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC059_DisposedContextConnection;

/// <summary>
/// Leftover 5.15.0 LC059 arms the original 21 analyzer cases do not isolate.
/// Unwrapping <c>IConditionalAccessInstanceOperation</c> to the conditional
/// receiver FPs only the <c>?.</c> quiet pins. Skipping conversion unwrap on
/// a <c>Dispose</c> receiver FNs only the interface-cast pin; the existing
/// <c>using ((DbConnection)...)</c> fixture stays green. The unreduced static
/// spelling is the same <c>IInvocationOperation.TargetMethod</c> as fluent
/// <c>GetDbConnection()</c>, and is pinned so that form stays in the corpus.
/// </summary>
public partial class DisposedContextConnectionTests
{
    [Fact]
    public async Task UnreducedStaticGetDbConnection_UsingDeclaration_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"using var connection = {|#0:RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database)|}; await connection.OpenAsync(ct);"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task UnreducedStaticGetDbConnection_Dispose_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"{|#0:RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database).Dispose()|};"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task DisposableCastDispose_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"var connection = db.Database.GetDbConnection(); {|#0:((IDisposable)connection).Dispose()|};"),
            VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task ConditionalDispose_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"var connection = db.Database.GetDbConnection(); connection?.Dispose();"));
    }

    [Fact]
    public async Task ConditionalDisposeAsync_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"var connection = db.Database.GetDbConnection(); _ = connection?.DisposeAsync();"));
    }
}
