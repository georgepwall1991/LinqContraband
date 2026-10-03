using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy.TransactionUnderRetryingStrategyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Leftover 5.16.0 LC063 <c>StrategyCallers</c> arms the original caller-lambda cases do not isolate.
/// <c>IsInsideNameof</c> drops <c>nameof(Save)</c> so a logging mention does not look like an unprotected
/// caller. <c>MemberBindingExpressionSyntax</c> treats <c>target?.Save()</c> as the invocation it is.
/// <c>IsAwaitPoint</c> treats <c>await foreach</c> and both <c>await using</c> forms as a suspension, so
/// a transaction after one in an un-awaited in-place async lambda still reports.
/// Factory method-group custom strategies stay a documented false negative; do not lock them as quiet.
/// </summary>
public class TransactionUnderRetryingStrategyCoverageTests
{
    private static Task VerifyAsync(string source) => VerifyCS.VerifyAnalyzerAsync(source);

    private const string ProtectedSave = @"
    private void SaveInTransaction()
    {
        using var tx = _db.Database.BeginTransaction();
        _db.SaveChanges();
        tx.Commit();
    }";

    private const string ReportedSave = @"
    private void SaveInTransaction()
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        _db.SaveChanges();
        tx.Commit();
    }";

    private const string EmptyAsync = @"
    private static async IAsyncEnumerable<int> EmptyAsync()
    {
        yield break;
    }";

    [Theory]
    [InlineData("_ = nameof(SaveInTransaction);")]
    [InlineData("_ = nameof(Service.SaveInTransaction);")]
    public async Task Nameof_BesideStrategyCaller_StaysQuiet(string nameofUse)
    {
        // nameof is not a call. Without the skip it is an unprotected method-group reference.
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(SaveInTransaction);
        " + nameofUse,
            extraMembers: ProtectedSave));
    }

    [Fact]
    public async Task Nameof_DoesNotHideUnprotectedCaller_Reports()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"_ = nameof(SaveInTransaction);
        SaveInTransaction();",
            extraMembers: ReportedSave));
    }

    [Fact]
    public async Task ConditionalAccessHelperCall_InsideStrategy_StaysQuiet()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"Service target = this;
        var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => target?.SaveInTransaction());",
            extraMembers: ProtectedSave));
    }

    [Fact]
    public async Task ConditionalAccessHelperCall_OutsideStrategy_Reports()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"Service target = this;
        target?.SaveInTransaction();",
            extraMembers: ReportedSave));
    }

    [Fact]
    public async Task AwaitForeach_BeforeHelperInUnawaitedInPlaceLambda_Reports()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => { _ = ((Func<Task>)(async () => { await foreach (var _ in EmptyAsync()) { } SaveInTransaction(); }))(); });",
            extraMembers: ReportedSave + EmptyAsync));
    }

    [Fact]
    public async Task AwaitUsingStatement_BeforeHelperInUnawaitedInPlaceLambda_Reports()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => { _ = ((Func<Task>)(async () => { await using (var _ = new System.IO.MemoryStream()) { } SaveInTransaction(); }))(); });",
            extraMembers: ReportedSave));
    }

    [Fact]
    public async Task AwaitUsingDeclaration_BeforeHelperInUnawaitedInPlaceLambda_Reports()
    {
        await VerifyAsync(TransactionUnderRetryingStrategyTests.Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => { _ = ((Func<Task>)(async () => { await using var _ = new System.IO.MemoryStream(); SaveInTransaction(); }))(); });",
            extraMembers: ReportedSave));
    }
}
