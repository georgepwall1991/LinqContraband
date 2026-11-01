using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC039_NestedSaveChanges.NestedSaveChangesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC039_NestedSaveChanges;

/// <summary>
/// Leftover <c>TransactionBoundaryMethodNames</c> the shipped fixtures do not isolate.
/// <c>BeginTransaction</c> and <c>BeginTransactionAsync</c> already sit between
/// saves or inside a transaction <c>using</c>. Dropping a leftover name fails
/// only that name's pin. A lookalike <c>CreateSavepoint</c> still reports so a
/// later edit that treats every savepoint-shaped helper as an EF boundary is
/// visible. <c>DbContextTransaction</c> as a leftover type name is not isolatable
/// from <c>IDbContextTransaction</c> and stays unpinned.
/// </summary>
public partial class NestedSaveChangesTests
{
    private static string BoundaryBetweenSaves(string boundaryCall, bool async = false)
    {
        if (async)
        {
            return EFCoreMock + Types + $@"

class Program
{{
    async Task Run()
    {{
        var db = new TestApp.AppDbContext();
        var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        {boundaryCall}
        await db.SaveChangesAsync();
    }}
}}";
        }

        return EFCoreMock + Types + $@"

class Program
{{
    void Run()
    {{
        var db = new TestApp.AppDbContext();
        var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        {boundaryCall}
        db.SaveChanges();
    }}
}}";
    }

    [Fact]
    public async Task Commit_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves("tx.Commit();"));
    }

    [Fact]
    public async Task CommitAsync_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves("await tx.CommitAsync();", async: true));
    }

    [Fact]
    public async Task Rollback_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves("tx.Rollback();"));
    }

    [Fact]
    public async Task RollbackAsync_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves("await tx.RollbackAsync();", async: true));
    }

    [Fact]
    public async Task CreateSavepoint_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"tx.CreateSavepoint(""sp"");"));
    }

    [Fact]
    public async Task CreateSavepointAsync_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"await tx.CreateSavepointAsync(""sp"");", async: true));
    }

    [Fact]
    public async Task ReleaseSavepoint_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"tx.ReleaseSavepoint(""sp"");"));
    }

    [Fact]
    public async Task ReleaseSavepointAsync_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"await tx.ReleaseSavepointAsync(""sp"");", async: true));
    }

    [Fact]
    public async Task RollbackToSavepoint_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"tx.RollbackToSavepoint(""sp"");"));
    }

    [Fact]
    public async Task RollbackToSavepointAsync_BetweenSaves_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(BoundaryBetweenSaves(@"await tx.RollbackToSavepointAsync(""sp"");", async: true));
    }

    [Fact]
    public async Task LookalikeCreateSavepoint_BetweenSaves_Reports()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        CreateSavepoint(""sp"");
        {|LC039:db.SaveChanges()|};
    }

    void CreateSavepoint(string name)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
