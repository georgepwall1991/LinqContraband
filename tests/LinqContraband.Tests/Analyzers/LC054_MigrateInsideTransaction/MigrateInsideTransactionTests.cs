using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC054_MigrateInsideTransaction.MigrateInsideTransactionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC054_MigrateInsideTransaction;

public class MigrateInsideTransactionTests
{
    private const string Usings = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
";

    // EF Core 9 added RelationalEventId.MigrationsUserTransactionWarning; the rule is gated on it.
    private const string Ef9Marker = @"
namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public static class RelationalEventId
    {
        public static readonly int MigrationsUserTransactionWarning = 20210;
        public static readonly int PendingModelChangesWarning = 20211;
    }

    public class WarningsConfigurationBuilder
    {
        public WarningsConfigurationBuilder Ignore(params int[] eventIds) => this;
        public WarningsConfigurationBuilder Log(params int[] eventIds) => this;
        public WarningsConfigurationBuilder Throw(params int[] eventIds) => this;
    }
}";

    private const string Ef8Diagnostics = @"
namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public static class RelationalEventId
    {
        public static readonly int PendingModelChangesWarning = 20211;
    }
}";

    private const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
        public void Dispose() { }
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static void Migrate(this Infrastructure.DatabaseFacade databaseFacade) { }
        public static void Migrate(this Infrastructure.DatabaseFacade databaseFacade, string targetMigration) { }
        public static Task MigrateAsync(this Infrastructure.DatabaseFacade databaseFacade, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public static IDbContextTransaction BeginTransaction(this Infrastructure.DatabaseFacade databaseFacade, System.Data.IsolationLevel isolationLevel) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade
    {
        public IDbContextTransaction BeginTransaction() => null;
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IDbContextTransaction>(null);
        public void CommitTransaction() { }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RollbackTransaction() { }
        public IDbContextTransaction CurrentTransaction => null;
        public IExecutionStrategy CreateExecutionStrategy() => null;
    }

    public interface IExecutionStrategy
    {
        void Execute(Action operation);
        Task ExecuteAsync(Func<Task> operation);
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    public interface IDbContextTransaction : IDisposable, IAsyncDisposable
    {
        void Commit();
        Task CommitAsync(CancellationToken cancellationToken = default);
        void Rollback();
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}

namespace System.Transactions
{
    public sealed class TransactionScope : IDisposable
    {
        public void Complete() { }
        public void Dispose() { }
    }
}

class AppDbContext : DbContext { }

static class Seeder
{
    public static void Seed(AppDbContext db) { }
    public static void Commit(IDbContextTransaction transaction) => transaction.Commit();
}";

    internal static string Wrap(string body, string extraMembers = "", bool ef9 = true) => Usings + @"
class Startup
{
    private readonly AppDbContext _db = new AppDbContext();
    private AppDbContext Db { get; } = new AppDbContext();
    private AppDbContext Fresh => new AppDbContext();
" + extraMembers + @"

    async Task Run(AppDbContext db, AppDbContext other, CancellationToken ct)
    {
" + body + @"
    }
}
" + EfMock + (ef9 ? Ef9Marker : Ef8Diagnostics);

    [Fact]
    public async Task UsingDeclaration_SyncMigrate_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        tx.Commit();"));
    }

    [Fact]
    public async Task AwaitUsingDeclaration_InsideExecutionStrategy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.{|LC054:MigrateAsync|}(ct);
            await tx.CommitAsync(ct);
        });"));
    }

    [Fact]
    public async Task UsingStatement_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using (var tx = db.Database.BeginTransaction())
        {
            db.Database.{|LC054:Migrate|}(""Initial"");
            tx.Commit();
        }"));
    }

    [Fact]
    public async Task UsingStatementWithoutLocal_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using (db.Database.BeginTransaction())
        {
            db.Database.{|LC054:Migrate|}();
        }"));
    }

    [Fact]
    public async Task PlainLocalAndIsolationLevelOverload_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        db.Database.{|LC054:Migrate|}();
        tx.Commit();"));
    }

    [Fact]
    public async Task AssignedLocal_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        IDbContextTransaction tx;
        tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        tx.Commit();"));
    }

    [Fact]
    public async Task FacadeTransactionWithoutLocal_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();
        db.Database.CommitTransaction();"));
    }

    [Fact]
    public async Task OtherWorkBetween_AndNestedBlock_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = db.Database.BeginTransaction();
        Console.WriteLine(""migrating"");
        if (ct.CanBeCanceled)
        {
            db.Database.{|LC054:Migrate|}();
        }
        tx.Commit();"));
    }

    [Theory]
    [InlineData("_db")]
    [InlineData("this._db")]
    [InlineData("Db")]
    public async Task FieldAndAutoPropertyContexts_Report(string context)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap($@"
        using var tx = {context}.Database.BeginTransaction();
        {context}.Database.{{|LC054:Migrate|}}();
        tx.Commit();"));
    }

    [Fact]
    public async Task InsideDbContext_ThisDatabase_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Usings + @"
class MigratingContext : DbContext
{
    public void MigrateNow()
    {
        using var tx = Database.BeginTransaction();
        Database.{|LC054:Migrate|}();
        tx.Commit();
    }
}
" + EfMock + Ef9Marker);
    }

    [Fact]
    public async Task ConfiguringOtherWarnings_StillReports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        new WarningsConfigurationBuilder().Ignore(RelationalEventId.PendingModelChangesWarning);
        new WarningsConfigurationBuilder().Throw(RelationalEventId.MigrationsUserTransactionWarning);
        using var tx = db.Database.BeginTransaction();
        db.Database.{|LC054:Migrate|}();"));
    }

    [Fact]
    public async Task BeforeEfCore9_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = db.Database.BeginTransaction();
        db.Database.Migrate();
        tx.Commit();", ef9: false));
    }

    [Theory]
    [InlineData("Ignore")]
    [InlineData("Log")]
    public async Task WarningDowngradedInProject_NoDiagnostic(string method)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap($@"
        new WarningsConfigurationBuilder().{method}(RelationalEventId.MigrationsUserTransactionWarning);
        using var tx = db.Database.BeginTransaction();
        db.Database.Migrate();
        tx.Commit();"));
    }

    [Fact]
    public async Task MigrateWithoutTransaction_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        await db.Database.MigrateAsync(ct);"));
    }

    [Fact]
    public async Task TransactionScope_NoDiagnostic()
    {
        // EF Core suppresses the ambient transaction while migrating, so TransactionScope does not trip the check.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using (var scope = new TransactionScope())
        {
            db.Database.Migrate();
            scope.Complete();
        }"));
    }

    [Theory]
    // Committed, rolled back, or disposed before Migrate.
    [InlineData(@"
        var tx = db.Database.BeginTransaction();
        Seeder.Seed(db);
        tx.Commit();
        db.Database.Migrate();")]
    [InlineData(@"
        var tx = db.Database.BeginTransaction();
        tx.Rollback();
        db.Database.Migrate();")]
    [InlineData(@"
        var tx = db.Database.BeginTransaction();
        tx.Dispose();
        db.Database.Migrate();")]
    [InlineData(@"
        db.Database.BeginTransaction();
        db.Database.CommitTransaction();
        db.Database.Migrate();")]
    [InlineData(@"
        db.Database.BeginTransaction();
        db.Database.CurrentTransaction?.Commit();
        db.Database.Migrate();")]
    // Handed to other code that may commit it.
    [InlineData(@"
        var tx = db.Database.BeginTransaction();
        Seeder.Commit(tx);
        db.Database.Migrate();")]
    // The using scope has ended.
    [InlineData(@"
        using (var tx = db.Database.BeginTransaction())
        {
            Seeder.Seed(db);
            tx.Commit();
        }
        db.Database.Migrate();")]
    [InlineData(@"
        {
            using var tx = db.Database.BeginTransaction();
            tx.Commit();
        }
        db.Database.Migrate();")]
    // Committed later in the same loop iteration, so the next iteration migrates outside it.
    [InlineData(@"
        var tx = db.Database.BeginTransaction();
        for (var i = 0; i < 2; i++)
        {
            db.Database.Migrate();
            tx.Commit();
        }")]
    // Conditionally begun.
    [InlineData(@"
        IDbContextTransaction tx = null;
        if (ct.CanBeCanceled) tx = db.Database.BeginTransaction();
        db.Database.Migrate();")]
    [InlineData(@"
        var tx = ct.CanBeCanceled ? db.Database.BeginTransaction() : null;
        db.Database.Migrate();")]
    public async Task TransactionEndedOrUnproven_NoDiagnostic(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Theory]
    // A different context has its own connection and transaction.
    [InlineData(@"
        using var tx = other.Database.BeginTransaction();
        db.Database.Migrate();")]
    // A computed property may return a new context on each read.
    [InlineData(@"
        using var tx = Fresh.Database.BeginTransaction();
        Fresh.Database.Migrate();")]
    // The local now points at another context.
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        db = other;
        db.Database.Migrate();")]
    public async Task DifferentOrUnprovenContext_NoDiagnostic(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Theory]
    // The transaction is outside the lambda, which may run after it ends.
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        Action migrate = () => db.Database.Migrate();
        tx.Commit();
        migrate();")]
    [InlineData(@"
        using var tx = db.Database.BeginTransaction();
        tx.Commit();
        void MigrateLater() => db.Database.Migrate();
        MigrateLater();")]
    public async Task MigrateInDeferredCode_NoDiagnostic(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task LookalikeMigrateAndBeginTransaction_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        using var tx = BeginTransaction(db);
        Migrate(db);", @"
    static IDbContextTransaction BeginTransaction(AppDbContext db) => null;
    static void Migrate(AppDbContext db) { }"));
    }
}
