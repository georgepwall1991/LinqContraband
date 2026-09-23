using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC054_MigrateInsideTransaction.MigrateInsideTransactionAnalyzer,
    LinqContraband.Analyzers.LC054_MigrateInsideTransaction.MigrateInsideTransactionFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC054_MigrateInsideTransaction;

public class MigrateInsideTransactionFixerTests
{
    private static string Wrap(string body) => MigrateInsideTransactionTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Fact]
    public async Task RemovesUsingDeclarationAndCommit()
    {
        await VerifyFixAsync(@"
        Console.WriteLine(""start"");
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        tx.Commit();
        Console.WriteLine(""done"");", @"
        Console.WriteLine(""start"");
        db.Database.Migrate();
        Console.WriteLine(""done"");");
    }

    [Fact]
    public async Task RemovesAsyncTransactionInsideExecutionStrategy()
    {
        await VerifyFixAsync(@"
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.{|LC054:MigrateAsync|}(ct);
            await tx.CommitAsync(ct);
        });", @"
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await db.Database.MigrateAsync(ct);
        });");
    }

    [Fact]
    public async Task RemovesTransactionWithoutCommitWhenMigrateIsLast()
    {
        await VerifyFixAsync(@"
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();", @"
        db.Database.Migrate();");
    }

    [Fact]
    public async Task RemovesFacadeBeginAndCommitTransaction()
    {
        await VerifyFixAsync(@"
        db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        db.Database.CommitTransaction();", @"
        db.Database.Migrate();");
    }

    [Fact]
    public async Task UnwrapsUsingStatement()
    {
        await VerifyFixAsync(@"
        using (var tx = db.Database.BeginTransaction())
        {
            db.Database.{|LC054:Migrate|}();
            tx.Commit();
        }
        Console.WriteLine(""done"");", @"
        db.Database.Migrate();
        Console.WriteLine(""done"");");
    }

    [Fact]
    public async Task UnwrapsUsingStatementWithEmbeddedStatement()
    {
        await VerifyFixAsync(@"
        using (db.Database.BeginTransaction())
            db.Database.{|LC054:Migrate|}();", @"
        db.Database.Migrate();");
    }

    [Fact]
    public async Task FixAll_RemovesEveryMigrationOnlyTransaction()
    {
        var before = Wrap(@"
        {
            using var tx = db.Database.BeginTransaction();
            db.Database.{|LC054:Migrate|}();
            tx.Commit();
        }
        {
            using var otherTx = other.Database.BeginTransaction();
            other.Database.{|LC054:Migrate|}();
        }");
        var after = Wrap(@"
        {
            db.Database.Migrate();
        }
        {
            other.Database.Migrate();
        }");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    [Theory]
    // Other work shares the transaction.
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        Seeder.Seed(db);
        tx.Commit();")]
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        Seeder.Seed(db);
        db.Database.{|LC054:Migrate|}();
        tx.Commit();")]
    [InlineData(@"
        using (var tx = db.Database.BeginTransaction())
        {
            db.Database.{|LC054:Migrate|}();
            Seeder.Seed(db);
            tx.Commit();
        }")]
    // Migrate is nested, not a statement next to the transaction.
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        if (ct.CanBeCanceled)
        {
            db.Database.{|LC054:Migrate|}();
        }
        tx.Commit();")]
    // Removing the assignment would leave the declaration unused.
    [InlineData(@"
        IDbContextTransaction tx;
        tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        tx.Commit();")]
    // The transaction is used again after the commit.
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        tx.Commit();
        tx.Dispose();")]
    public async Task TransactionCoversMoreThanMigrate_NoFix(string code)
    {
        await VerifyNoFixAsync(code);
    }
}
