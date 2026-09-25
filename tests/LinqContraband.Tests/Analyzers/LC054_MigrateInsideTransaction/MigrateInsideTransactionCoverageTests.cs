using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC054_MigrateInsideTransaction.MigrateInsideTransactionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC054_MigrateInsideTransaction;

/// <summary>
/// Leftover 5.10.0 arms the original 35 analyzer cases do not isolate.
/// Treating a surrounding loop as already ended keeps the existing
/// commit-inside-the-same-iteration quiet pin green and only fails the
/// report cases below. Dropping <c>RollbackTransaction</c> from the
/// facade-end list keeps <c>tx.Rollback()</c> (a local reference) green
/// and only fails the facade pins.
/// </summary>
public partial class MigrateInsideTransactionTests
{
    [Fact]
    public async Task ForLoopWithOpenTransaction_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = db.Database.BeginTransaction();
        for (var i = 0; i < 2; i++)
        {
            db.Database.{|LC054:Migrate|}();
        }
        tx.Commit();"));
    }

    [Fact]
    public async Task WhileLoopWithOpenTransaction_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = db.Database.BeginTransaction();
        var i = 0;
        while (i++ < 2)
        {
            db.Database.{|LC054:Migrate|}();
        }
        tx.Commit();"));
    }

    [Fact]
    public async Task FacadeRollbackTransaction_ThenMigrate_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        db.Database.BeginTransaction();
        db.Database.RollbackTransaction();
        db.Database.Migrate();"));
    }

    [Fact]
    public async Task FacadeRollbackTransactionAsync_ThenMigrate_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        await db.Database.BeginTransactionAsync(ct);
        await db.Database.RollbackTransactionAsync(ct);
        await db.Database.MigrateAsync(ct);"));
    }
}
