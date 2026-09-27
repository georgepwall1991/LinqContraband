using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC062_BlockingEfAsyncCall.BlockingEfAsyncCallAnalyzer>;

using LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

namespace LinqContraband.Tests.Analyzers.LC062_BlockingEfAsyncCall;

public class BlockingEfAsyncCallTests
{
    internal const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
";

    /// <summary>EF Core's async and sync APIs with the signatures EF Core 8 declares.</summary>
    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
        public virtual int SaveChanges() => 0;
        public virtual int SaveChanges(bool acceptAllChangesOnSuccess) => 0;
        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public virtual Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public virtual void AddRange(params object[] entities) { }
        public virtual Task AddRangeAsync(params object[] entities) => Task.CompletedTask;
        public virtual object Find(Type entityType, params object[] keyValues) => null;
        public virtual ValueTask<object> FindAsync(Type entityType, params object[] keyValues) => default;
    }

    public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;

        public virtual TEntity Find(params object[] keyValues) => null;
        public virtual ValueTask<TEntity> FindAsync(params object[] keyValues) => default;
        public virtual ValueTask<TEntity> FindAsync(object[] keyValues, CancellationToken cancellationToken) => default;
        public virtual ChangeTracking.EntityEntry<TEntity> Add(TEntity entity) => null;
        public virtual ValueTask<ChangeTracking.EntityEntry<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default) => default;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, System.Linq.Expressions.Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task ForEachAsync<T>(this IQueryable<T> source, Action<T> action, CancellationToken cancellationToken = default) => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
        public static Task<int> ExecuteDeleteAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static void Migrate(this Infrastructure.DatabaseFacade databaseFacade) { }
        public static Task MigrateAsync(this Infrastructure.DatabaseFacade databaseFacade, CancellationToken cancellationToken = default) => null;
        public static int ExecuteSqlRaw(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
        public static int ExecuteSqlRaw(this Infrastructure.DatabaseFacade databaseFacade, string sql, IEnumerable<object> parameters) => 0;
        public static Task<int> ExecuteSqlRawAsync(this Infrastructure.DatabaseFacade databaseFacade, string sql, CancellationToken cancellationToken = default) => null;
        public static Task<int> ExecuteSqlRawAsync(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => null;
        public static Task<int> ExecuteSqlRawAsync(this Infrastructure.DatabaseFacade databaseFacade, string sql, IEnumerable<object> parameters, CancellationToken cancellationToken = default) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade
    {
        public virtual Storage.IDbContextTransaction BeginTransaction() => null;
        public virtual Task<Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => null;
        public virtual bool EnsureCreated() => false;
        public virtual Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    public interface IDbContextTransaction : IDisposable { }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    public class EntityEntry<TEntity> where TEntity : class { }
}

public class User { public int Id { get; set; } public string Name { get; set; } }

public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
{
    public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    public Task<List<User>> LoadUsersAsync() => null;
}

public class AuditedContext : ShopContext
{
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => base.SaveChangesAsync(cancellationToken);
}

public static class Helpers
{
    public static Task<int> GetNumberAsync() => Task.FromResult(1);
}
";

    internal static string Wrap(string body, bool isAsync = false) => Usings + @"
class Program
{
    private Task<List<User>> _pending;

    " + (isAsync ? "async Task" : "void") + @" Run(ShopContext db, AuditedContext audited, int id, CancellationToken ct)
    {
        " + body + @"
    }
}
" + EfMock;

    /// <summary>An LC062 at markup location 0, whatever the message arguments.</summary>
    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult Reported() =>
        new Microsoft.CodeAnalysis.Testing.DiagnosticResult(
            BlockingEfAsyncCallAnalyzer.DiagnosticId,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).WithLocation(0);

    public static readonly TheoryData<string> ReportedShapes = new()
    {
        @"var users = {|#0:db.Users.ToListAsync().Result|};",
        @"{|#0:db.SaveChangesAsync().Wait()|};",
        @"var user = {|#0:db.Users.FirstOrDefaultAsync(x => x.Id == id).GetAwaiter().GetResult()|};",
        @"var user = {|#0:db.Users.FindAsync(id).Result|};",
        @"{|#0:db.Users.FindAsync(id).AsTask().Wait()|};",
        @"var user = {|#0:db.Users.FindAsync(new object[] { id }, ct).AsTask().Result|};",
        @"var saved = {|#0:db.SaveChangesAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult()|};",
        @"var saved = {|#0:db.SaveChangesAsync(true, ct).Result|};",
        @"{|#0:db.Database.MigrateAsync(ct).Wait()|};",
        @"{|#0:db.Database.MigrateAsync(ct).GetAwaiter().GetResult()|};",
        @"var transaction = {|#0:db.Database.BeginTransactionAsync(ct).Result|};",
        @"var created = {|#0:db.Database.EnsureCreatedAsync().Result|};",
        @"var rows = {|#0:db.Database.ExecuteSqlRawAsync(""DELETE FROM Users"", ct).Result|};",
        @"var rows = {|#0:db.Database.ExecuteSqlRawAsync(""DELETE FROM Users WHERE Id = {0}"", id).Result|};",
        @"var rows = {|#0:db.Users.Where(x => x.Id > id).ExecuteDeleteAsync(ct).Result|};",
        @"var entry = {|#0:db.Users.AddAsync(new User(), ct).Result|};",
        @"{|#0:db.AddRangeAsync(new User(), new User()).Wait()|};",
        @"var count = {|#0:db.Users.ToListAsync().Result|}.Count;",
        @"var any = {|#0:db.Users.AnyAsync(ct).Result|} && id > 0;",
        @"{|#0:db.Users.ForEachAsync(u => { }).Wait()|};",
        @"var saved = {|#0:audited.SaveChangesAsync().Result|};",
        @"var task = db.Users.ToListAsync(ct); var users = {|#0:task.Result|};",
        @"Task<int> save; save = db.SaveChangesAsync(); {|#0:save.Wait()|};",
        @"var find = db.Users.FindAsync(id); var user = {|#0:find.Result|};",
        @"Func<List<User>> load = () => {|#0:db.Users.ToListAsync().Result|};",
    };

    [Theory]
    [MemberData(nameof(ReportedShapes))]
    public async Task BlockingOnEfAsyncCall_InSyncMethod_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), Reported());
    }

    [Theory]
    [MemberData(nameof(ReportedShapes))]
    public async Task BlockingOnEfAsyncCall_InAsyncMethod_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body, isAsync: true), Reported());
    }

    [Theory]
    [InlineData(@"var users = {|#0:db.Users.ToListAsync().Result|};", ".Result", "ToListAsync")]
    [InlineData(@"{|#0:db.SaveChangesAsync().Wait()|};", ".Wait()", "SaveChangesAsync")]
    [InlineData(@"{|#0:db.Database.MigrateAsync().GetAwaiter().GetResult()|};", ".GetAwaiter().GetResult()", "MigrateAsync")]
    public async Task Message_NamesTheBlockingAccessAndTheEfCall(string body, string blocking, string efMethod)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0).WithArguments(blocking, efMethod));
    }

    [Fact]
    public async Task WaitWithTimeout_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"var done = {|#0:db.SaveChangesAsync().Wait(TimeSpan.FromSeconds(5))|};"),
            Reported());
    }

    [Fact]
    public async Task WaitThenResult_ReportsTheWaitOnly()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"var task = db.Users.ToListAsync(); {|#0:task.Wait()|}; var users = task.Result;"),
            Reported());
    }

    [Theory]
    // Awaited.
    [InlineData(@"var users = await db.Users.ToListAsync(ct);")]
    [InlineData(@"var task = db.Users.ToListAsync(); await task; var users = task.Result;")]
    [InlineData(@"var task = db.Users.ToListAsync(); await task.ConfigureAwait(false); var users = task.GetAwaiter().GetResult();")]
    // Completion proven by a combinator.
    [InlineData(@"var users = db.Users.ToListAsync(); var count = db.Users.CountAsync(); await Task.WhenAll(users, count); var list = users.Result; var n = count.Result;")]
    [InlineData(@"var users = db.Users.ToListAsync(); await Task.WhenAll(new Task[] { users }); var list = users.Result;")]
    // Completion checked first.
    [InlineData(@"var task = db.Users.ToListAsync(); var users = task.IsCompletedSuccessfully ? task.Result : null;")]
    [InlineData(@"var task = db.Users.ToListAsync(); if (task.IsCompleted) { var users = task.Result; }")]
    // Any await in between may have completed it.
    [InlineData(@"var task = db.Users.ToListAsync(); await Task.Delay(1, ct); var users = task.Result;")]
    public async Task CompletedTasks_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body, isAsync: true));
    }

    [Theory]
    // Not EF Core.
    [InlineData(@"var n = Task.FromResult(1).Result;")]
    [InlineData(@"var n = Helpers.GetNumberAsync().Result;")]
    [InlineData(@"var users = db.LoadUsersAsync().Result;")]
    [InlineData(@"Task.Delay(10).Wait();")]
    // In-memory queries (LC060's case: the async operator throws on them).
    [InlineData(@"var users = new List<User>().AsQueryable().ToListAsync().Result;")]
    [InlineData(@"var query = new List<User>().AsQueryable().Where(u => u.Id > id); var n = query.CountAsync().Result;")]
    // The task is not proven to come from EF Core.
    [InlineData(@"var users = _pending.Result;")]
    [InlineData(@"var users = Task.Run(() => db.Users.ToListAsync()).Result;")]
    [InlineData(@"var task = db.Users.ToListAsync(); if (id > 0) task = Task.FromResult(new List<User>()); var users = task.Result;")]
    [InlineData(@"var task = db.Users.ToListAsync(); var copy = task; var users = copy.Result;")]
    [InlineData(@"var task = db.Users.ToListAsync(); Func<List<User>> read = () => task.Result;")]
    public async Task OtherTasks_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }

    [Fact]
    public async Task ResultAfterAwaitInAsyncLambda_DoesNotReport()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        Func<Task<int>> count = async () =>
        {
            var task = db.Users.CountAsync(ct);
            await task;
            return task.Result;
        };"));
    }
}
