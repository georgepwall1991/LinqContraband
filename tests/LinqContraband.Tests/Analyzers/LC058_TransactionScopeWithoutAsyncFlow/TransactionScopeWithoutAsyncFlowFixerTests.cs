using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow.TransactionScopeWithoutAsyncFlowAnalyzer,
    LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow.TransactionScopeWithoutAsyncFlowFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC058_TransactionScopeWithoutAsyncFlow;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class TransactionScopeWithoutAsyncFlowFixerTests
{
    private static string Wrap(string body) => TransactionScopeWithoutAsyncFlowTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    /// <summary>The fixer-coverage corpus: every reported constructor shape gets a compiling fix or none.</summary>
    [Theory]
    [InlineData(
        @"using (var scope = {|LC058:new TransactionScope()|}) { await db.SaveChangesAsync(ct); scope.Complete(); }",
        @"using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)) { await db.SaveChangesAsync(ct); scope.Complete(); }")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope()|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using ({|LC058:new TransactionScope(TransactionScopeOption.Required)|}) { await db.SaveChangesAsync(ct); }",
        @"using (new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled)) { await db.SaveChangesAsync(ct); }")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(TransactionScopeOption.Required, options)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(TransactionScopeOption.Required, options, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(TransactionScopeOption.RequiresNew, timeout)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(TransactionScopeOption.RequiresNew, timeout, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(Transaction.Current)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(Transaction.Current, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(Transaction.Current, timeout)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(Transaction.Current, timeout, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(TransactionScopeAsyncFlowOption.Suppress)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Suppress)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using TransactionScope scope = {|LC058:new()|}; await db.SaveChangesAsync(ct);",
        @"using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using var scope = {|LC058:new TransactionScope(scopeOption: TransactionScopeOption.Required)|}; await db.SaveChangesAsync(ct);",
        @"using var scope = new TransactionScope(scopeOption: TransactionScopeOption.Required, asyncFlowOption: TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(
        @"using (var scope = {|LC058:new TransactionScope()|}) { await foreach (var row in db.Stream()) { } }",
        @"using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)) { await foreach (var row in db.Stream()) { } }")]
    public async Task AddsAsyncFlowOption(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }

    [Fact]
    public async Task FullyQualifiedScope_AddsTheUsing()
    {
        const string before = @"
using System.Threading.Tasks;

class Program
{
    async Task Run(Task work)
    {
        using (var scope = {|LC058:new System.Transactions.TransactionScope()|})
        {
            await work;
            scope.Complete();
        }
    }
}
";
        const string after = @"
using System.Threading.Tasks;
using System.Transactions;

class Program
{
    async Task Run(Task work)
    {
        using (var scope = new System.Transactions.TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await work;
            scope.Complete();
        }
    }
}
";
        await new CodeFixTest { TestCode = before, FixedCode = after }.RunAsync();
    }

    [Fact]
    public async Task NoAsyncFlowOverload_OffersNoFix()
    {
        var code = Wrap(@"using var scope = {|LC058:new TransactionScope(TransactionScopeOption.Required, options, EnterpriseServicesInteropOption.None)|}; await db.SaveChangesAsync(ct);");
        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task FixAll_FixesEveryScope()
    {
        var before = Wrap(@"
        using (var first = {|LC058:new TransactionScope()|}) { await db.SaveChangesAsync(ct); first.Complete(); }
        using (var second = {|LC058:new TransactionScope(TransactionScopeOption.RequiresNew)|}) { await db.SaveChangesAsync(ct); second.Complete(); }");
        var after = Wrap(@"
        using (var first = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)) { await db.SaveChangesAsync(ct); first.Complete(); }
        using (var second = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled)) { await db.SaveChangesAsync(ct); second.Complete(); }");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }
}
