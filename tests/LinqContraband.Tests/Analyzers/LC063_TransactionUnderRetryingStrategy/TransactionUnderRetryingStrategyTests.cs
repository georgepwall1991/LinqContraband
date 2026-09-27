using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy.TransactionUnderRetryingStrategyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC063_TransactionUnderRetryingStrategy;

public class TransactionUnderRetryingStrategyTests
{
    internal const string Usings = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
";

    // Mirrors the EF Core, relational and SQL Server provider API shapes the rule relies on.
    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
        protected virtual void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() { }
    }

    public class DbContextOptions { }
    public class DbContextOptions<TContext> : DbContextOptions where TContext : DbContext { }

    public class DbContextOptionsBuilder
    {
        public DbContextOptions Options => null;
    }

    public class DbContextOptionsBuilder<TContext> : DbContextOptionsBuilder where TContext : DbContext
    {
        public new DbContextOptions<TContext> Options => null;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static IDbContextTransaction BeginTransaction(this Infrastructure.DatabaseFacade databaseFacade, IsolationLevel isolationLevel) => null;
        public static Task<IDbContextTransaction> BeginTransactionAsync(this Infrastructure.DatabaseFacade databaseFacade, IsolationLevel isolationLevel, CancellationToken cancellationToken = default) => null;
        public static IDbContextTransaction UseTransaction(this Infrastructure.DatabaseFacade databaseFacade, DbTransaction transaction) => null;
        public static Task<IDbContextTransaction> UseTransactionAsync(this Infrastructure.DatabaseFacade databaseFacade, DbTransaction transaction, CancellationToken cancellationToken = default) => null;
    }

    public static class ExecutionStrategyExtensions
    {
        public static void Execute(this IExecutionStrategy strategy, Action operation) { }
        public static TResult Execute<TResult>(this IExecutionStrategy strategy, Func<TResult> operation) => default;
        public static Task ExecuteAsync(this IExecutionStrategy strategy, Func<Task> operation) => Task.CompletedTask;
        public static Task<TResult> ExecuteAsync<TResult>(this IExecutionStrategy strategy, Func<Task<TResult>> operation) => null;
        public static void ExecuteInTransaction(this IExecutionStrategy strategy, Action operation, Func<bool> verifySucceeded) { }
        public static Task ExecuteInTransactionAsync(this IExecutionStrategy strategy, Func<Task> operation, Func<Task<bool>> verifySucceeded) => Task.CompletedTask;
    }

    public static class SqlServerDbContextOptionsExtensions
    {
        public static DbContextOptionsBuilder UseSqlServer(this DbContextOptionsBuilder optionsBuilder, string connectionString, Action<Infrastructure.SqlServerDbContextOptionsBuilder> sqlServerOptionsAction = null) => optionsBuilder;
        public static DbContextOptionsBuilder<TContext> UseSqlServer<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, string connectionString, Action<Infrastructure.SqlServerDbContextOptionsBuilder> sqlServerOptionsAction = null) where TContext : DbContext => optionsBuilder;
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade
    {
        public IDbContextTransaction BeginTransaction() => null;
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IDbContextTransaction>(null);
        public IExecutionStrategy CreateExecutionStrategy() => null;
    }

    public abstract class RelationalDbContextOptionsBuilder<TBuilder, TExtension>
        where TBuilder : RelationalDbContextOptionsBuilder<TBuilder, TExtension>
    {
        public virtual TBuilder ExecutionStrategy(Func<ExecutionStrategyDependencies, IExecutionStrategy> getExecutionStrategy) => (TBuilder)this;
        public virtual TBuilder CommandTimeout(int? commandTimeout) => (TBuilder)this;
    }

    public class SqlServerOptionsExtension { }

    public class SqlServerDbContextOptionsBuilder : RelationalDbContextOptionsBuilder<SqlServerDbContextOptionsBuilder, SqlServerOptionsExtension>
    {
        public virtual SqlServerDbContextOptionsBuilder EnableRetryOnFailure() => this;
        public virtual SqlServerDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount) => this;
        public virtual SqlServerDbContextOptionsBuilder EnableRetryOnFailure(ICollection<int> errorNumbersToAdd) => this;
        public virtual SqlServerDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount, TimeSpan maxRetryDelay, ICollection<int> errorNumbersToAdd) => this;
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    public interface IDbContextTransaction : IDisposable, IAsyncDisposable
    {
        void Commit();
        Task CommitAsync(CancellationToken cancellationToken = default);
    }

    public interface IExecutionStrategy
    {
        bool RetriesOnFailure { get; }
    }

    public sealed class ExecutionStrategyDependencies { }

    public abstract class ExecutionStrategy : IExecutionStrategy
    {
        protected ExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount, TimeSpan maxRetryDelay) { }
        public bool RetriesOnFailure => true;
        protected abstract bool ShouldRetryOn(Exception exception);
    }

    public sealed class NonRetryingExecutionStrategy : IExecutionStrategy
    {
        public NonRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies) { }
        public bool RetriesOnFailure => false;
    }

    public class SqlServerRetryingExecutionStrategy : ExecutionStrategy
    {
        public SqlServerRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies) : base(dependencies, 6, TimeSpan.FromSeconds(30)) { }
        public SqlServerRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount) : base(dependencies, maxRetryCount, TimeSpan.FromSeconds(30)) { }
        protected override bool ShouldRetryOn(Exception exception) => true;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }

    public static class EntityFrameworkServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services, Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction = null) where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
        public static IServiceCollection AddDbContext<TContextService, TContextImplementation>(this IServiceCollection services, Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction = null) where TContextImplementation : Microsoft.EntityFrameworkCore.DbContext, TContextService => services;
        public static IServiceCollection AddDbContextPool<TContext>(this IServiceCollection services, Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction, int poolSize = 1024) where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
        public static IServiceCollection AddDbContextFactory<TContext>(this IServiceCollection services, Action<IServiceProvider, Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction) where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
        public static IServiceCollection AddPooledDbContextFactory<TContext>(this IServiceCollection services, Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction, int poolSize = 1024) where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
    }
}

public interface IAppDb { }
public class AppDb : Microsoft.EntityFrameworkCore.DbContext, IAppDb { }
public class ReportingDb : Microsoft.EntityFrameworkCore.DbContext { }
public class Order { }
";

    internal const string AppDbRetry = @"
static class Registration
{
    public static void Configure(IServiceCollection services) =>
        services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));
}
";

    internal static string Wrap(string body, string configuration = AppDbRetry, string extraMembers = "") => Usings + @"
class Service
{
    private readonly AppDb _db = new AppDb();
" + extraMembers + @"

    async Task Run(AppDb db, ReportingDb reporting, DbTransaction external, CancellationToken ct)
    {
" + body + @"
    }
}
" + configuration + EfMock;

    private static Task VerifyAsync(string source) => VerifyCS.VerifyAnalyzerAsync(source);

    [Theory]
    [InlineData(@"await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);")]
    [InlineData(@"using var tx = db.Database.{|LC063:BeginTransaction|}(); db.SaveChanges(); tx.Commit();")]
    [InlineData(@"using (var tx = db.Database.{|LC063:BeginTransaction|}()) { db.SaveChanges(); tx.Commit(); }")]
    [InlineData(@"using var tx = db.Database.{|LC063:BeginTransaction|}(IsolationLevel.Serializable); db.SaveChanges(); tx.Commit();")]
    [InlineData(@"await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(IsolationLevel.Serializable, ct); await tx.CommitAsync(ct);")]
    [InlineData(@"var tx = db.Database.{|LC063:BeginTransaction|}(); try { db.SaveChanges(); tx.Commit(); } finally { tx.Dispose(); }")]
    [InlineData(@"using var tx = _db.Database.{|LC063:BeginTransaction|}(); _db.SaveChanges(); tx.Commit();")]
    [InlineData(@"db.Database.{|LC063:UseTransaction|}(external); db.SaveChanges();")]
    [InlineData(@"await db.Database.{|LC063:UseTransactionAsync|}(external, ct); await db.SaveChangesAsync(ct);")]
    [InlineData(@"using var tx = RelationalDatabaseFacadeExtensions.{|LC063:BeginTransaction|}(db.Database, IsolationLevel.ReadCommitted); tx.Commit();")]
    public async Task TransactionOnRetryingContext_Reports(string body)
    {
        await VerifyAsync(Wrap(body));
    }

    [Fact]
    public async Task ReportMessage_NamesTheMethodAndContext()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"using var tx = db.Database.{|#0:BeginTransaction|}(); tx.Commit();"),
            VerifyCS.Diagnostic().WithLocation(0).WithOptions(DiagnosticOptions.IgnoreAdditionalLocations)
                .WithArguments("BeginTransaction", "AppDb"));
    }

    [Theory]
    // The retrying configuration belongs to AppDb; ReportingDb has none.
    [InlineData(@"using var tx = reporting.Database.BeginTransaction(); reporting.SaveChanges(); tx.Commit();")]
    // Inside the execution strategy, the transaction retries as one unit.
    [InlineData(@"await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { await using var tx = await db.Database.BeginTransactionAsync(ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); });")]
    [InlineData(@"var strategy = db.Database.CreateExecutionStrategy(); strategy.Execute(() => { using var tx = db.Database.BeginTransaction(); db.SaveChanges(); tx.Commit(); });")]
    [InlineData(@"var strategy = db.Database.CreateExecutionStrategy(); var saved = strategy.Execute(() => { using var tx = db.Database.BeginTransaction(); var n = db.SaveChanges(); tx.Commit(); return n; });")]
    [InlineData(@"var strategy = db.Database.CreateExecutionStrategy(); await strategy.ExecuteAsync(async () => { await Task.Yield(); using var tx = db.Database.BeginTransaction(); tx.Commit(); });")]
    [InlineData(@"var strategy = db.Database.CreateExecutionStrategy(); strategy.ExecuteInTransaction(() => { using var tx = db.Database.BeginTransaction(); tx.Commit(); }, () => true);")]
    [InlineData(@"var strategy = db.Database.CreateExecutionStrategy(); await strategy.ExecuteAsync(async () => { await Inner(); async Task Inner() { await using var tx = await db.Database.BeginTransactionAsync(ct); await tx.CommitAsync(ct); } });")]
    // Clearing the transaction is not starting one.
    [InlineData(@"db.Database.UseTransaction(null); db.SaveChanges();")]
    // A lambda the code stores could be handed to a strategy later.
    [InlineData(@"Func<Task> work = async () => { await using var tx = await db.Database.BeginTransactionAsync(ct); await tx.CommitAsync(ct); };")]
    public async Task NotReported(string body)
    {
        await VerifyAsync(Wrap(body));
    }

    [Fact]
    public async Task NoRetryingConfiguration_NotReported()
    {
        await VerifyAsync(Wrap(
            @"using var tx = db.Database.BeginTransaction(); db.SaveChanges(); tx.Commit();",
            configuration: @"
static class Registration
{
    public static void Configure(IServiceCollection services) =>
        services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.CommandTimeout(30)));
}
"));
    }

    [Theory]
    [InlineData(@"sql => sql.EnableRetryOnFailure(0)")]
    [InlineData(@"sql => sql.EnableRetryOnFailure(maxRetryCount: 0)")]
    [InlineData(@"sql => sql.EnableRetryOnFailure(0, TimeSpan.FromSeconds(5), null)")]
    [InlineData(@"sql => sql.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d))")]
    [InlineData(@"sql => sql.ExecutionStrategy(d => new SqlServerRetryingExecutionStrategy(d, 0))")]
    public async Task RetriesTurnedOff_NotReported(string providerOptions)
    {
        await VerifyAsync(Wrap(
            @"using var tx = db.Database.BeginTransaction(); tx.Commit();",
            configuration: @"
static class Registration
{
    public static void Configure(IServiceCollection services) =>
        services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", " + providerOptions + @"));
}
"));
    }

    [Theory]
    [InlineData(@"services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure(5)));")]
    [InlineData(@"services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure(maxRetryCount: retries)));")]
    [InlineData(@"services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure(new List<int> { 4060 })));")]
    [InlineData(@"services.AddDbContext<IAppDb, AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));")]
    [InlineData(@"services.AddDbContextPool<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));")]
    [InlineData(@"services.AddDbContextFactory<AppDb>((sp, o) => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));")]
    [InlineData(@"services.AddPooledDbContextFactory<AppDb>(o => o.UseSqlServer(""cs"", sql => { sql.CommandTimeout(30); sql.EnableRetryOnFailure(); }));")]
    [InlineData(@"services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.ExecutionStrategy(d => new SqlServerRetryingExecutionStrategy(d))));")]
    [InlineData(@"services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.ExecutionStrategy(d => new CustomRetryingStrategy(d))));")]
    [InlineData(@"var options = new DbContextOptionsBuilder<AppDb>().UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()).Options;")]
    [InlineData(@"if (retries > 0) services.AddDbContext<AppDb>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));")]
    public async Task RegistrationShapes_Report(string registration)
    {
        await VerifyAsync(Wrap(
            @"using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit();",
            configuration: @"
static class Registration
{
    public static void Configure(IServiceCollection services, int retries)
    {
        " + registration + @"
    }
}

class CustomRetryingStrategy : ExecutionStrategy
{
    public CustomRetryingStrategy(ExecutionStrategyDependencies dependencies) : base(dependencies, 3, TimeSpan.FromSeconds(10)) { }
    protected override bool ShouldRetryOn(Exception exception) => true;
}
"));
    }

    [Fact]
    public async Task OnConfiguringOverride_Reports()
    {
        await VerifyAsync(Wrap(
            @"using var tx = reporting.Database.{|LC063:BeginTransaction|}(); tx.Commit();
        using var other = db.Database.BeginTransaction(); other.Commit();",
            configuration: "").Replace(
            "public class ReportingDb : Microsoft.EntityFrameworkCore.DbContext { }",
            @"public class ReportingDb : Microsoft.EntityFrameworkCore.DbContext
{
    protected override void OnConfiguring(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure());
}"));
    }

    [Fact]
    public async Task DerivedFromConfiguredContext_Reports()
    {
        await VerifyAsync(Wrap(
            @"using var tx = derived.Database.{|LC063:BeginTransaction|}(); tx.Commit();",
            extraMembers: "private readonly DerivedAppDb derived = new DerivedAppDb();") + @"
public class DerivedAppDb : AppDb { }
");
    }

    [Fact]
    public async Task BaseTypedReceiver_NotReported()
    {
        // The static type DbContext may be any context; the rule cannot tell which.
        await VerifyAsync(Wrap(
            @"DbContext any = db; using var tx = any.Database.BeginTransaction(); tx.Commit();"));
    }

    [Fact]
    public async Task HelperConfiguration_SingleContext_Reports()
    {
        var source = Usings + @"
class Service
{
    void Run(AppDb db)
    {
        using var tx = db.Database.{|LC063:BeginTransaction|}();
        db.SaveChanges();
        tx.Commit();
    }
}

static class DatabaseOptions
{
    public static void Apply(DbContextOptionsBuilder options) => options.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure());
}
" + EfMock.Replace("public class ReportingDb : Microsoft.EntityFrameworkCore.DbContext { }", "public abstract class ContextBase : Microsoft.EntityFrameworkCore.DbContext { }");
        await VerifyAsync(source);
    }

    [Fact]
    public async Task HelperConfiguration_SeveralContexts_NotReported()
    {
        await VerifyAsync(Wrap(
            @"using var tx = db.Database.BeginTransaction(); tx.Commit();",
            configuration: @"
static class DatabaseOptions
{
    public static void Apply(DbContextOptionsBuilder options) => options.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure());
}
"));
    }

    [Fact]
    public async Task MethodCalledFromStrategyLambda_NotReported()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => SaveInTransactionAsync(db, ct));
        strategy.Execute(SaveInTransaction);",
            extraMembers: @"
    private static async Task SaveInTransactionAsync(AppDb db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void SaveInTransaction()
    {
        using var tx = _db.Database.BeginTransaction();
        _db.SaveChanges();
        tx.Commit();
    }"));
    }

    [Fact]
    public async Task HelperEvaluatedBeforeExecute_Reports()
    {
        // BuildWork() runs before the strategy does; only the delegate it returns runs under the strategy.
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(BuildWork());
        await strategy.ExecuteAsync(() => { var work = BuildAsyncWork(); return work(); });",
            extraMembers: @"
    private Action BuildWork()
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        tx.Commit();
        return () => _db.SaveChanges();
    }

    private Func<Task> BuildAsyncWork() => () => _db.SaveChangesAsync();"));
    }

    [Fact]
    public async Task MethodGroupOnlyCapturedInsideStrategyLambda_Reports()
    {
        // Storing the method group does not run it under the strategy.
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => { Action later = SaveInTransaction; GC.KeepAlive(later); });",
            extraMembers: @"
    private void SaveInTransaction()
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        _db.SaveChanges();
        tx.Commit();
    }"));
    }

    [Fact]
    public async Task CalleeChainUnderStrategy_NotReported()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => Outer());",
            extraMembers: @"
    private void Outer()
    {
        Validate();
        Inner();
    }

    private void Validate() { }

    private void Inner()
    {
        using var tx = _db.Database.BeginTransaction();
        _db.SaveChanges();
        tx.Commit();
    }"));
    }

    [Fact]
    public async Task CalleeChainWithAnUnprotectedCaller_Reports()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => Outer());
        Inner();",
            extraMembers: @"
    private void Outer() => Inner();

    private void Inner()
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        _db.SaveChanges();
        tx.Commit();
    }"));
    }

    [Fact]
    public async Task RecursiveCallersWithoutAStrategyEntry_Report()
    {
        // A cycle of callers proves nothing unless one of them is entered from the strategy.
        await VerifyAsync(Wrap(
            @"Ping(3);",
            extraMembers: @"
    private void Ping(int n) { if (n > 0) Pong(n - 1); }

    private void Pong(int n)
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        tx.Commit();
        Ping(n);
        Pong(n - 1);
    }"));
    }

    [Fact]
    public async Task MutualRecursionEnteredOnlyFromStrategy_NotReported()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => A(3));",
            extraMembers: @"
    private void A(int n) { if (n > 0) B(n - 1); }

    private void B(int n)
    {
        using var tx = _db.Database.BeginTransaction();
        tx.Commit();
        A(n);
    }"));
    }

    [Fact]
    public async Task MutualRecursionWithAnUnprotectedEntry_Reports()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() => A(3));
        B(1);",
            extraMembers: @"
    private void A(int n) { if (n > 0) B(n - 1); }

    private void B(int n)
    {
        using var tx = _db.Database.{|LC063:BeginTransaction|}();
        tx.Commit();
        A(n);
    }"));
    }

    [Fact]
    public async Task ConstructedGenericContexts_AreKeptApart()
    {
        await VerifyAsync(Wrap(
            @"using var audit = auditDb.Database.BeginTransaction(); audit.Commit();
        using var customers = customerDb.Database.{|LC063:BeginTransaction|}(); customers.Commit();",
            configuration: @"
static class Registration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddDbContext<TenantDb<Customer>>(o => o.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()));
        services.AddDbContext<TenantDb<Audit>>(o => o.UseSqlServer(""cs""));
        var options = new DbContextOptionsBuilder<TenantDb<Customer>>().UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure()).Options;
    }
}

public class TenantDb<T> : DbContext { }
public class Customer { }
public class Audit { }
",
            extraMembers: @"
    private readonly TenantDb<Customer> customerDb = new TenantDb<Customer>();
    private readonly TenantDb<Audit> auditDb = new TenantDb<Audit>();"));
    }

    [Fact]
    public async Task OpenGenericOnConfiguring_AppliesToEveryConstruction()
    {
        await VerifyAsync(Wrap(
            @"using var audit = auditDb.Database.{|LC063:BeginTransaction|}(); audit.Commit();
        using var customers = customerDb.Database.{|LC063:BeginTransaction|}(); customers.Commit();",
            configuration: @"
public class TenantDb<T> : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlServer(""cs"", sql => sql.EnableRetryOnFailure());
}

public class Customer { }
public class Audit { }
",
            extraMembers: @"
    private readonly TenantDb<Customer> customerDb = new TenantDb<Customer>();
    private readonly TenantDb<Audit> auditDb = new TenantDb<Audit>();"));
    }

    [Fact]
    public async Task EnableRetryOnFailureOnANonEfBuilder_NotReported()
    {
        await VerifyAsync(Wrap(
            @"using var tx = db.Database.BeginTransaction(); tx.Commit();",
            configuration: @"
static class Registration
{
    public static void Configure(IServiceCollection services) =>
        services.AddDbContext<AppDb>(o =>
        {
            new CacheOptionsBuilder().EnableRetryOnFailure();
            o.UseSqlServer(""cs"");
        });
}

class CacheOptionsBuilder
{
    public CacheOptionsBuilder EnableRetryOnFailure() => this;
    public CacheOptionsBuilder ExecutionStrategy(Func<ExecutionStrategyDependencies, IExecutionStrategy> factory) => this;
}
"));
    }

    [Fact]
    public async Task MethodThatOnlyMentionsExecutionStrategy_IsNotAWrapper()
    {
        await VerifyAsync(Wrap(
            @"Run(() =>
        {
            using var tx = db.Database.{|LC063:BeginTransaction|}();
            db.SaveChanges();
            tx.Commit();
        });",
            extraMembers: @"
    private static void Run(Action action)
    {
        Console.WriteLine(""ExecutionStrategy"");
        action();
    }"));
    }

    [Fact]
    public async Task WrapperWithAFallbackThatInvokesTheDelegate_Reports()
    {
        // The fallback branch runs the delegate outside the strategy, so Run is not a wrapper.
        await VerifyAsync(Wrap(
            @"Run(() =>
        {
            using var tx = db.Database.{|LC063:BeginTransaction|}();
            tx.Commit();
        }, ct.CanBeCanceled);",
            extraMembers: @"
    private void Run(Action action, bool resilient)
    {
        if (resilient)
            _db.Database.CreateExecutionStrategy().Execute(action);
        else
            action();
    }"));
    }

    [Theory]
    // Stored, or captured by a lambda that is not the strategy's delegate: it can run outside the strategy.
    [InlineData(@"_last = action; _db.Database.CreateExecutionStrategy().Execute(action);")]
    [InlineData(@"_db.Database.CreateExecutionStrategy().Execute(action); Task.Run(() => action());")]
    [InlineData(@"_db.Database.CreateExecutionStrategy().Execute(() => { Action inner = () => action(); inner(); });")]
    public async Task WrapperWithAnotherUseOfTheDelegate_Reports(string runBody)
    {
        await VerifyAsync(Wrap(
            @"Run(() =>
        {
            using var tx = db.Database.{|LC063:BeginTransaction|}();
            tx.Commit();
        });",
            extraMembers: @"
    private Action _last;

    private void Run(Action action)
    {
        " + runBody + @"
    }"));
    }

    [Fact]
    public async Task WrapperForwardingTheDelegateToTheStrategy_NotReported()
    {
        await VerifyAsync(Wrap(
            @"Run(() =>
        {
            using var tx = db.Database.BeginTransaction();
            db.SaveChanges();
            tx.Commit();
        });
        RunLogged(() =>
        {
            using var tx = db.Database.BeginTransaction();
            tx.Commit();
        }, ""save"");",
            extraMembers: @"
    private void Run(Action action) => _db.Database.CreateExecutionStrategy().Execute(action);

    private void RunLogged(Action action, string name)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            Console.WriteLine(name);
            action();
        });
    }"));
    }

    [Theory]
    [InlineData(@"Action work = () => { using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit(); }; work();")]
    [InlineData(@"Action work; work = () => { using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit(); }; work.Invoke();")]
    [InlineData(@"((Action)(() => { using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit(); }))();")]
    [InlineData(@"Action work = () => { using var tx = db.Database.{|LC063:BeginTransaction|}(); tx.Commit(); }; db.Database.CreateExecutionStrategy().Execute(() => work()); work();")]
    public async Task LambdaInvokedInPlace_Reports(string body)
    {
        await VerifyAsync(Wrap(body));
    }

    [Theory]
    // The local is invoked only under the strategy.
    [InlineData(@"Action work = () => { using var tx = db.Database.BeginTransaction(); tx.Commit(); }; db.Database.CreateExecutionStrategy().Execute(() => work());")]
    // Handed to a method the rule cannot see into: where it runs is unknown.
    [InlineData(@"Action work = () => { using var tx = db.Database.BeginTransaction(); tx.Commit(); }; Schedule(work);")]
    public async Task LambdaWithUnknownOrProtectedInvocation_NotReported(string body)
    {
        await VerifyAsync(Wrap(body, extraMembers: "    private static void Schedule(Action action) { }"));
    }

    [Fact]
    public async Task LocalFunctionOnlyCalledInsideStrategyLambda_NotReported()
    {
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            void Save()
            {
                using var tx = db.Database.BeginTransaction();
                tx.Commit();
            }

            Save();
        });"));
    }

    [Fact]
    public async Task LocalFunctionEscapingTheStrategyLambda_Reports()
    {
        await VerifyAsync(Wrap(
            @"Action escaped = null;
        var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            void Save()
            {
                using var tx = db.Database.{|LC063:BeginTransaction|}();
                tx.Commit();
            }

            escaped = Save;
        });
        escaped();"));
    }

    [Fact]
    public async Task LambdaHandedToAnotherMethodInsideStrategyLambda_Reports()
    {
        // Task.Run's delegate runs on its own schedule, not as part of the strategy's operation.
        await VerifyAsync(Wrap(
            @"var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            Task.Run(() =>
            {
                using var tx = db.Database.{|LC063:BeginTransaction|}();
                tx.Commit();
            });
        });"));
    }

    [Fact]
    public async Task ProjectResilientTransactionWrapper_NotReported()
    {
        await VerifyAsync(Wrap(
            @"await ResilientTransaction.New(db).ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });") + @"
class ResilientTransaction
{
    private readonly DbContext _context;
    private ResilientTransaction(DbContext context) => _context = context;
    public static ResilientTransaction New(DbContext context) => new ResilientTransaction(context);

    public async Task ExecuteAsync(Func<Task> action)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(action);
    }
}
");
    }

    [Fact]
    public async Task LambdaPassedToOtherMethod_Reports()
    {
        await VerifyAsync(Wrap(
            @"await Task.Run(async () =>
        {
            await using var tx = await db.Database.{|LC063:BeginTransactionAsync|}(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });"));
    }

    [Fact]
    public async Task NoEfCore_NotReported()
    {
        await VerifyAsync(@"
class DatabaseFacade { public object BeginTransaction() => null; }
class Db { public DatabaseFacade Database { get; } = new DatabaseFacade(); }
class C { void M(Db db) { db.Database.BeginTransaction(); } }
");
    }
}
