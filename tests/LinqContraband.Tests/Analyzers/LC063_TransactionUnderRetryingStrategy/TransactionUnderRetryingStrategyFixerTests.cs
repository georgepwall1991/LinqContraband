using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy.TransactionUnderRetryingStrategyAnalyzer,
    LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy.TransactionUnderRetryingStrategyFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class TransactionUnderRetryingStrategyFixerTests
{
    private static string Wrap(string body, string extraMembers = "") =>
        TransactionUnderRetryingStrategyTests.Wrap(body, extraMembers: extraMembers);

    private static Task VerifyFixAsync(string before, string after, string extraMembers = "")
    {
        return new CodeFixTest { TestCode = Wrap(before, extraMembers), FixedCode = Wrap(after, extraMembers) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code, string extraMembers = "")
    {
        return new CodeFixTest { TestCode = Wrap(code, extraMembers), FixedCode = Wrap(code, extraMembers) }.RunAsync();
    }

    [Fact]
    public async Task AwaitUsingDeclaration_WrapsTheRestOfTheBlockInExecuteAsync()
    {
        await VerifyFixAsync(@"
        var order = new Order();
        // Save the order and its lines together.
        await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);", @"
        var order = new Order();
        // Save the order and its lines together.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });");
    }

    [Fact]
    public async Task SyncUsingDeclaration_InSyncMethod_UsesExecute()
    {
        var before = TransactionUnderRetryingStrategyTests.Wrap("await Task.CompletedTask;", extraMembers: @"
    void Save(AppDb db)
    {
        using var tx = db.Database.{|LC063:BeginTransaction|}(IsolationLevel.Serializable);
        db.SaveChanges();
        tx.Commit();
    }");
        var after = TransactionUnderRetryingStrategyTests.Wrap("await Task.CompletedTask;", extraMembers: @"
    void Save(AppDb db)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            db.SaveChanges();
            tx.Commit();
        });
    }");
        await new CodeFixTest { TestCode = before, FixedCode = after }.RunAsync();
    }

    [Fact]
    public async Task SyncTransaction_InAsyncMethodWithoutAwaits_UsesExecute()
    {
        await VerifyFixAsync(@"
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        _db.SaveChanges();
        tx.Commit();", @"
        var strategy = _db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            using var tx = _db.Database.BeginTransaction();
            _db.SaveChanges();
            tx.Commit();
        });");
    }

    [Fact]
    public async Task SyncTransaction_FollowedByAwaits_UsesExecuteAsync()
    {
        await VerifyFixAsync(@"
        using var tx = db.Database.{|LC063:BeginTransaction|}();
        await db.SaveChangesAsync(ct);
        tx.Commit();", @"
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var tx = db.Database.BeginTransaction();
            await db.SaveChangesAsync(ct);
            tx.Commit();
        });");
    }

    [Fact]
    public async Task UsingStatement_WrapsOnlyTheStatement()
    {
        await VerifyFixAsync(@"
        var saved = 0;
        using (var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct).ConfigureAwait(false))
        {
            saved = await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        Console.WriteLine(saved);", @"
        var saved = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
            {
                saved = await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        });
        Console.WriteLine(saved);");
    }

    [Fact]
    public async Task NestedBlock_WrapsToTheEndOfThatBlock()
    {
        await VerifyFixAsync(@"
        if (ct.CanBeCanceled)
        {
            await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
            await tx.CommitAsync(ct);
        }
        await db.SaveChangesAsync(ct);", @"
        if (ct.CanBeCanceled)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        await db.SaveChangesAsync(ct);");
    }

    [Fact]
    public async Task StrategyNameTaken_PicksAnother()
    {
        await VerifyFixAsync(@"
        var strategy = ""fast"";
        await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
        await tx.CommitAsync(ct);", @"
        var strategy = ""fast"";
        var executionStrategy = db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await tx.CommitAsync(ct);
        });");
    }

    [Fact]
    public async Task InsideAnotherLambda_WrapsInsideThatLambda()
    {
        await VerifyFixAsync(@"
        await Task.Run(async () =>
        {
            await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(IsolationLevel.Serializable, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });", @"
        await Task.Run(async () =>
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        });");
    }

    [Fact]
    public async Task FixAll_FixesEveryTransaction()
    {
        var before = Wrap(@"
        if (ct.CanBeCanceled)
        {
            await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            using var tx = db.Database.{|LC063:BeginTransaction|}();
            tx.Commit();
        }");
        var after = Wrap(@"
        if (ct.CanBeCanceled)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        else
        {
            var strategy = db.Database.CreateExecutionStrategy();
            strategy.Execute(() =>
            {
                using var tx = db.Database.BeginTransaction();
                tx.Commit();
            });
        }");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    [Theory]
    // A return inside the moved code would only leave the lambda.
    [InlineData(@"if (ct.IsCancellationRequested) return; await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct); if (ct.IsCancellationRequested) return; await tx.CommitAsync(ct);")]
    // Not a using declaration of its own.
    [InlineData(@"var tx = db.Database.{|LC063:BeginTransaction|}(); try { db.SaveChanges(); tx.Commit(); } finally { tx.Dispose(); }")]
    [InlineData(@"using var tx = RelationalDatabaseFacadeExtensions.{|LC063:BeginTransaction|}(db.Database, IsolationLevel.ReadCommitted); tx.Commit();")]
    [InlineData(@"db.Database.{|LC063:UseTransaction|}(external); db.SaveChanges();")]
    [InlineData(@"await db.Database.{|LC063:UseTransactionAsync|}(external, ct); await db.SaveChangesAsync(ct);")]
    [InlineData(@"using IDisposable tx = db.Database.{|LC063:BeginTransaction|}(), other = null;")]
    [InlineData(@"if (ct.CanBeCanceled) using (var tx = db.Database.{|LC063:BeginTransaction|}()) { tx.Commit(); }")]
    // The context expression is re-evaluated for CreateExecutionStrategy(), so it has to be stable.
    [InlineData(@"using var tx = Fresh.Database.{|LC063:BeginTransaction|}(); tx.Commit();")]
    // The moved code breaks out of the loop around it; inside a lambda that does not compile.
    [InlineData(@"while (true) { using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit(); break; }")]
    // A local assigned inside the moved code and read after it is not definitely assigned any more.
    [InlineData(@"int saved; using (var tx = db.Database.{|LC063:BeginTransaction|}()) { saved = db.SaveChanges(); tx.Commit(); } Console.WriteLine(saved);")]
    public async Task UnsupportedShape_NoFix(string code)
    {
        await VerifyNoFixAsync(code, extraMembers: "private AppDb Fresh => new AppDb();");
    }

    [Fact]
    public async Task RefParameter_NoFix()
    {
        await VerifyNoFixAsync(
            "await Task.CompletedTask;",
            extraMembers: @"
    void Save(AppDb db, ref int saved)
    {
        using var tx = db.Database.{|LC063:BeginTransaction|}();
        saved = db.SaveChanges();
        tx.Commit();
    }");
    }

    // Inside a child namespace of Microsoft.EntityFrameworkCore, UseSqlServer binds without a using.
    private const string NamespacedRegistration = @"
namespace Microsoft.EntityFrameworkCore.Setup
{
    static class Registration
    {
        public static void Configure(IServiceCollection services) =>
            services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));
    }
}
";

    [Fact]
    public async Task MissingEfCoreUsing_IsAdded()
    {
        var before = TransactionUnderRetryingStrategyTests.Wrap(@"
        await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
        await tx.CommitAsync(ct);", configuration: NamespacedRegistration);
        var after = TransactionUnderRetryingStrategyTests.Wrap(@"
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await tx.CommitAsync(ct);
        });", configuration: NamespacedRegistration);
        const string EfUsing = "using Microsoft.EntityFrameworkCore;\n";
        await new CodeFixTest
        {
            TestCode = before.Replace(EfUsing, ""),
            FixedCode = after.Replace(EfUsing, "").Replace("using Microsoft.EntityFrameworkCore.Storage;", EfUsing + "using Microsoft.EntityFrameworkCore.Storage;")
        }.RunAsync();
    }
}
