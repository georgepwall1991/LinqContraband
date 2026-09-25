using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow.TransactionScopeWithoutAsyncFlowAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC058_TransactionScopeWithoutAsyncFlow;

public class TransactionScopeWithoutAsyncFlowTests
{
    internal const string Usings = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
";

    internal const string Types = @"
public class ShopContext
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public int SaveChanges() => 0;
    public IAsyncEnumerable<int> Stream() => null;
}
";

    internal static string Wrap(string body) => Usings + @"
class Program
{
    async Task Run(ShopContext db, TimeSpan timeout, TransactionOptions options, TransactionScopeAsyncFlowOption flow, CancellationToken ct)
    {
" + body + @"
    }

    void RunSync(ShopContext db)
    {
    }
}
" + Types;

    [Theory]
    [InlineData(@"using (var scope = {|#0:new TransactionScope()|}) { await db.SaveChangesAsync(ct); scope.Complete(); }")]
    [InlineData(@"using var scope = {|#0:new TransactionScope()|}; await db.SaveChangesAsync(ct); scope.Complete();")]
    [InlineData(@"using ({|#0:new TransactionScope(TransactionScopeOption.Required)|}) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using (var scope = {|#0:new TransactionScope(TransactionScopeOption.Required, options)|}) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using (var scope = {|#0:new TransactionScope(TransactionScopeOption.RequiresNew, timeout)|}) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using (var scope = {|#0:new TransactionScope(TransactionScopeAsyncFlowOption.Suppress)|}) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using (var scope = {|#0:new System.Transactions.TransactionScope()|}) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using TransactionScope scope = {|#0:new()|}; await db.SaveChangesAsync(ct);")]
    // await foreach keeps the scope alive across awaits too.
    [InlineData(@"using (var scope = {|#0:new TransactionScope()|}) { await foreach (var row in db.Stream()) { } }")]
    // The await is nested in the scope's block.
    [InlineData(@"using (var scope = {|#0:new TransactionScope()|}) { if (ct.CanBeCanceled) { await db.SaveChangesAsync(ct); } }")]
    public async Task ScopeAliveAcrossAwait_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0));
    }

    [Fact]
    public async Task AsyncLambda_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        Func<Task> work = async () =>
        {
            using var scope = {|LC058:new TransactionScope()|};
            await db.SaveChangesAsync(ct);
            scope.Complete();
        };
        await work();"));
    }

    [Fact]
    public async Task AsyncLocalFunction_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        await SaveAsync();

        async Task SaveAsync()
        {
            using (var scope = {|LC058:new TransactionScope(TransactionScopeOption.Required)|})
            {
                await db.SaveChangesAsync(ct);
                scope.Complete();
            }
        }"));
    }

    [Theory]
    // Async flow is enabled.
    [InlineData(@"using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)) { await db.SaveChangesAsync(ct); }")]
    [InlineData(@"using var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    [InlineData(@"using var scope = new TransactionScope(TransactionScopeOption.Required, options, TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);")]
    // The option comes from somewhere else: not known.
    [InlineData(@"using var scope = new TransactionScope(flow); await db.SaveChangesAsync(ct);")]
    // No await while the scope is alive.
    [InlineData(@"using (var scope = new TransactionScope()) { db.SaveChanges(); scope.Complete(); } await db.SaveChangesAsync(ct);")]
    [InlineData(@"await db.SaveChangesAsync(ct); using var scope = new TransactionScope(); db.SaveChanges(); scope.Complete();")]
    [InlineData(@"{ using var scope = new TransactionScope(); db.SaveChanges(); scope.Complete(); } await db.SaveChangesAsync(ct);")]
    // The await is inside a lambda that the scope does not wrap.
    [InlineData(@"using (var scope = new TransactionScope()) { Func<Task> later = async () => await db.SaveChangesAsync(ct); scope.Complete(); }")]
    // Not a using: the rule does not track manual disposal.
    [InlineData(@"var scope = new TransactionScope(); await db.SaveChangesAsync(ct); scope.Dispose();")]
    public async Task SafeShapes_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task SynchronousMethod_DoesNotReport()
    {
        await VerifyCS.VerifyAnalyzerAsync(Usings + @"
class Program
{
    void Run(ShopContext db)
    {
        using (var scope = new TransactionScope())
        {
            db.SaveChanges();
            scope.Complete();
        }
    }

    Task RunTask(ShopContext db)
    {
        using var scope = new TransactionScope();
        return db.SaveChangesAsync();
    }
}
" + Types);
    }
}
