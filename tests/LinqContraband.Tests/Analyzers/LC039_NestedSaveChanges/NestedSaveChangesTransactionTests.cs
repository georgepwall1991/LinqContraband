using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC039_NestedSaveChanges.NestedSaveChangesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC039_NestedSaveChanges;

public partial class NestedSaveChangesTests
{
    [Fact]
    public async Task TransactionBoundaryBetweenSaves_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSaveChangesInsideTransactionUsingBlock_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using (var tx = db.Database.BeginTransaction())
        {
            db.SaveChanges();
            db.SaveChanges();
            tx.Commit();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSaveChangesInsideCustomBeginTransactionUsingBlock_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using (BeginTransaction())
        {
            db.SaveChanges();
            {|LC039:db.SaveChanges()|};
        }
    }

    FakeTransaction BeginTransaction() => new FakeTransaction();

    private sealed class FakeTransaction : System.IDisposable
    {
        public void Dispose()
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideUsingDeclarationTransaction_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideAwaitUsingDeclarationTransaction_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    async Task Run()
    {
        var db = new TestApp.AppDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideUsingDeclarationTransactionWithinNestedBlock_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        using var tx = db.Database.BeginTransaction();
        if (flag)
        {
            db.SaveChanges();
            db.SaveChanges();
        }
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDeclarationOfTransactionAfterFirstSave_DoesNotSuppress()
    {
        // Sanity guardrail: a using-declaration that comes AFTER the first save
        // must not retroactively cover saves that preceded it. The existing
        // boundary-between-positions check already handles this (BeginTransaction
        // is between the two saves), so this should pass and stay quiet.
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
