using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsTests
{
    internal const string EfMock = @"
	#nullable enable
	using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public ValueTask<TEntity> FindAsync<TEntity>(params object[] keyValues) where TEntity : class => default;
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
        public DbSet<TEntity> Set<TEntity>(string name) where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
        public ValueTask<TEntity> FindAsync(params object[] keyValues) => default;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TEntity>> ToListAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TEntity>());

        public static Task<bool> AnyAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public static Task<bool> ContainsAsync<TEntity>(
            this IQueryable<TEntity> source,
            TEntity item,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public static Task<TEntity> ElementAtAsync<TEntity>(
            this IQueryable<TEntity> source,
            int index,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TEntity));

        public static Task<TEntity> ElementAtOrDefaultAsync<TEntity>(
            this IQueryable<TEntity> source,
            int index,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TEntity));

        public static Task<Dictionary<TKey, TEntity>> ToDictionaryAsync<TEntity, TKey>(
            this IQueryable<TEntity> source,
            Func<TEntity, TKey> keySelector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<TKey, TEntity>());

        public static Task LoadAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public static Task<int> ExecuteUpdateAsync<TEntity>(
            this IQueryable<TEntity> source,
            object setters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public static Task<int> ExecuteDeleteAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public static IAsyncEnumerable<TEntity> AsAsyncEnumerable<TEntity>(
            this IQueryable<TEntity> source) => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(
            this DbSet<TEntity> source,
            string sql,
            params object[] parameters) where TEntity : class => source;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static Task<int> ExecuteSqlRawAsync(
            this Infrastructure.DatabaseFacade database,
            string sql,
            params object[] parameters) =>
            Task.FromResult(0);

        public static Task<int> ExecuteSqlRawAsync(
            this Infrastructure.DatabaseFacade database,
            string sql,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public static Task<int> ExecuteSqlInterpolatedAsync(
            this Infrastructure.DatabaseFacade database,
            FormattableString sql,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public sealed class DatabaseFacade
    {
    }
}
";

    [Fact]
    public async Task TaskWhenAll_WithTwoQueriesOnSameContext_ShouldTriggerOnSecondOperation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Users.ToListAsync()|},
                {|#1:db.Users.ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithDirectLocalFunctionReturns_ShouldTriggerOnlyForCapturedContext()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task CapturedContext(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#0:Start()|},
                {|#1:Start()|});
        }

        public async Task FreshContextPerCall()
        {
            Task<bool> Start() => new AppDbContext().Users.AnyAsync();

            await Task.WhenAll(
                Start(),
                Start());
        }

        public async Task ParameterizedDifferentContexts(
            AppDbContext first,
            AppDbContext second)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                Start(first),
                Start(second));
        }

        public async Task HelperChain(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();
            Task<bool> Wrapped() => Start();

            await Task.WhenAll(
                Wrapped(),
                Wrapped());
        }

        public async Task ReassignedCapturedParameter(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();

            var first = Start();
            db = new AppDbContext();
            var second = Start();
            await Task.WhenAll(first, second);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithDirectParameterizedLocalFunctionReturns_ShouldTriggerForSameContextOrCapture()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                {|#0:Start(db)|},
                {|#1:Start(db)|});
        }

        public async Task CapturedContext(AppDbContext db, AppDbContext ignored)
        {
            Task<bool> Start(AppDbContext current) => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#2:Start(ignored)|},
                {|#3:Start(ignored)|});
        }
    }
}";

        var directParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var capturedContext = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, directParameter, capturedContext);
    }

    [Fact]
    public async Task TaskWhenAll_WithSafeNonContextParameterAndCapturedContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            await Task.WhenAll(
                {|#0:Load(0)|},
                {|#1:Load(1)|});
        }

        public async Task UnusedParameter(AppDbContext db)
        {
            Task<bool> Load(int ignored) => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#2:Load(0)|},
                {|#3:Load(1)|});
        }
    }
}";

        var queryParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var unusedParameter = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, queryParameter, unusedParameter);
    }

    [Fact]
    public async Task TaskWhenAll_WithTwoSafeParametersAndCapturedContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                {|#0:Load(0, CancellationToken.None)|},
                {|#1:Load(1, CancellationToken.None)|});
        }

        public async Task OptionalToken(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token = default) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                {|#2:Load(0)|},
                {|#3:Load(1)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var optionalToken = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, optionalToken);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenTwoParameterLocalFunctionCalls_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task ThrowingCallArgument(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(GetIndex(), CancellationToken.None),
                Load(GetIndex(), CancellationToken.None));
        }

        public async Task ThrowingParameterUse(AppDbContext db)
        {
            Task<User> Load(int divisor, CancellationToken token) =>
                db.Users.ElementAtAsync(10 / divisor, token);

            await Task.WhenAll(
                Load(1, CancellationToken.None),
                Load(2, CancellationToken.None));
        }

        public async Task ThrowingSecondCallArgument(AppDbContext db)
        {
            Task<User> Load(CancellationToken token, int index) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(CancellationToken.None, GetIndex()),
                Load(CancellationToken.None, GetIndex()));
        }

        public async Task ThrowingSecondParameterUse(AppDbContext db)
        {
            Task<User> Load(CancellationToken token, int divisor) =>
                db.Users.ElementAtAsync(10 / divisor, token);

            await Task.WhenAll(
                Load(CancellationToken.None, 1),
                Load(CancellationToken.None, 2));
        }

        public async Task DefinitelyCancelledToken(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(Load(0, canceled), Load(1, canceled));
        }

        public async Task ReassignedCapture(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            var first = Load(0, CancellationToken.None);
            db = new AppDbContext();
            var second = Load(1, CancellationToken.None);
            await Task.WhenAll(first, second);
        }

        public async Task FreshContext(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                new AppDbContext().Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(0, CancellationToken.None),
                Load(1, CancellationToken.None));
        }

        public async Task DirectContextParameter(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }

        public async Task ThreeParameters(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token, bool ignored) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(0, CancellationToken.None, true),
                Load(1, CancellationToken.None, true));
        }

        private static int GetIndex() => 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenOneParameterLocalFunctionCalls_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
        public int Index { get; }
    }

    public sealed class ContextHolder
    {
        public AppDbContext Context { get; } = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task FreshContext(AppDbContext db)
        {
            Task<User> Load(int index) =>
                new AppDbContext().Users.ElementAtAsync(index);

            await Task.WhenAll(Load(0), Load(1));
        }

        public async Task ReassignedCapture(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            var first = Load(0);
            db = new AppDbContext();
            var second = Load(1);
            await Task.WhenAll(first, second);
        }

        public async Task ThrowingArguments(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            await Task.WhenAll(Load(GetIndex()), Load(GetIndex()));
        }

        public async Task ThrowingParameterUse(AppDbContext db)
        {
            Task<User> Load(int divisor) =>
                db.Users.ElementAtAsync(10 / divisor);

            await Task.WhenAll(Load(0), Load(1));
        }

        public async Task ThrowingContextProperty(ContextHolder holder)
        {
            Task<bool> Load(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                Load(holder.Context),
                Load(holder.Context));
        }

        public async Task ThrowingUnusedContextArgument(AppDbContext db)
        {
            Task<bool> Load(AppDbContext ignored) => db.Users.AnyAsync();

            await Task.WhenAll(Load(CreateContext()), Load(CreateContext()));
        }

        public async Task ThrowingContextParameterUse(
            AppDbContext db,
            AppDbContext other)
        {
            Task<User> Load(AppDbContext current) =>
                db.Users.ElementAtAsync(current.Index);

            await Task.WhenAll(Load(other), Load(other));
        }

        private static int GetIndex() => 0;
        private static AppDbContext CreateContext() => new AppDbContext();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithParameterizedLocalFunctionOutsideDirectContextBinding_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task FreshInsteadOfParameter(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) =>
                new AppDbContext().Users.AnyAsync();

            await Task.WhenAll(
                Start(db),
                Start(db));
        }

        public async Task ReassignedArgument(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            var first = Start(db);
            db = new AppDbContext();
            var second = Start(db);
            await Task.WhenAll(first, second);
        }

        public async Task ReassignedCapturedContext(
            AppDbContext db,
            AppDbContext ignored)
        {
            Task<bool> Start(AppDbContext current) => db.Users.AnyAsync();

            var first = Start(ignored);
            db = new AppDbContext();
            var second = Start(ignored);
            await Task.WhenAll(first, second);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocals_WithSameContext_ShouldTriggerOnSecondOperation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            var second = {|#1:db.Users.ToListAsync()|};
            await Task.WhenAll(first, second);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DiscardedTask_ThenSameContextOperation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnawaitedQuery_ThenAwaitedSaveChanges_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var query = {|#0:db.Users.AnyAsync()|};
            await {|#1:db.SaveChangesAsync()|};
            await query;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithConcurrentQueries_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>().AnyAsync()|},
                {|#1:db.Set<User>().ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenName_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task NullName(DbContext db)
        {
            await Task.WhenAll(
                db.Set<User>((string)null).AnyAsync(),
                db.Set<User>((string)null).ToListAsync());
        }

        public async Task BlankName(DbContext db)
        {
            await Task.WhenAll(
                db.Set<User>(""   "").AnyAsync(),
                db.Set<User>(""   "").ToListAsync());
        }

    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenName_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db, string name)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(name).AnyAsync()|},
                {|#1:db.Set<User>(name).ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithProvenName_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                {|#1:db.Set<User>(""Users"").ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenNameInUnawaitedStatements_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var first = db.Set<User>((string)null).AnyAsync();
            var second = db.Set<User>((string)null).ToListAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithMixedNameValidity_ShouldStillReportValidPair()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                db.Set<User>((string)null).ToListAsync(),
                {|#1:db.Set<User>(""Users"").ContainsAsync(null)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithTrailingUnprovenName_ShouldStillReportValidPair()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                {|#1:db.Set<User>(""Users"").ToListAsync()|},
                db.Set<User>((string)null).ContainsAsync(null));
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithProvenLocalName_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            var name = ""Users"";
            await Task.WhenAll(
                {|#0:db.Set<User>(name).AnyAsync()|},
                {|#1:db.Set<User>(name).ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenNameHoistedToLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            var set = db.Set<User>((string)null);
            await Task.WhenAll(
                set.AnyAsync(),
                set.ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithNoNameArgument_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var first = {|#0:db.Set<User>().AnyAsync()|};
            var second = {|#1:db.Set<User>().ToListAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NonLoopOperations_WithUnprovenArgumentsFromParameters_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task SqlFromParameter(AppDbContext db, string sql)
        {
            await Task.WhenAll(
                {|#0:db.Database.ExecuteSqlRawAsync(sql)|},
                {|#1:db.Database.ExecuteSqlRawAsync(sql)|});
        }

        public async Task TokenFromParameter(AppDbContext db, CancellationToken ct)
        {
            await Task.WhenAll(
                {|#2:db.Users.AnyAsync(ct)|},
                {|#3:db.Users.ToListAsync(ct)|});
        }

    }
}";

        var sqlFromParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var tokenFromParameter = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, sqlFromParameter, tokenFromParameter);
    }

    [Fact]
    public async Task NonLoopOperations_WithValidRequiredArguments_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task RawSql(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Database.ExecuteSqlRawAsync(""SELECT 1"")|},
                {|#1:db.Database.ExecuteSqlRawAsync(""SELECT 2"")|});
        }

        public async Task QuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                {|#2:db.Users.FromSqlRaw(""SELECT 1"").AnyAsync()|},
                {|#3:db.Users.FromSqlRaw(""SELECT 2"").ToListAsync()|});
        }

        public void FindKeys(AppDbContext db)
        {
            var first = {|#4:db.FindAsync<User>(1)|};
            var second = {|#5:db.FindAsync<User>(2)|};
        }

        public async Task NonCancelledToken(AppDbContext db)
        {
            var token = new System.Threading.CancellationToken(false);
            await Task.WhenAll(
                {|#6:db.Users.AnyAsync(token)|},
                {|#7:db.Users.ToListAsync(token)|});
        }
    }
}";

        var rawSql = VerifyCS.Diagnostic().WithLocation(1).WithLocation(0).WithArguments("db");
        var querySql = VerifyCS.Diagnostic().WithLocation(3).WithLocation(2).WithArguments("db");
        var findKeys = VerifyCS.Diagnostic().WithLocation(5).WithLocation(4).WithArguments("db");
        var token = VerifyCS.Diagnostic().WithLocation(7).WithLocation(6).WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, rawSql, querySql, findKeys, token);
    }

    [Fact]
    public async Task NonLoopOperations_WithInvalidRequiredArguments_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task NullRawSql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Database.ExecuteSqlRawAsync((string)null),
                db.Database.ExecuteSqlRawAsync((string)null));
        }

        public async Task EmptyQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw("""").AnyAsync(),
                db.Users.FromSqlRaw("""").ToListAsync());
        }

        public async Task NullFindKeys(AppDbContext db)
        {
            var first = db.FindAsync<User>((object[])null);
            var second = db.FindAsync<User>((object[])null);
        }

        public async Task DefinitelyCancelled(AppDbContext db)
        {
            var canceled = new System.Threading.CancellationToken(true);
            await Task.WhenAll(
                db.Users.AnyAsync(canceled),
                db.Users.ToListAsync(canceled));
        }

        public async Task WhitespaceQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw(""   "").AnyAsync(),
                db.Users.FromSqlRaw(""   "").ToListAsync());
        }

        public async Task NullQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw((string)null).AnyAsync(),
                db.Users.FromSqlRaw((string)null).ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticEfExtensionSyntax_WithSameContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:EntityFrameworkQueryableExtensions.ToListAsync(db.Users)|},
                {|#1:EntityFrameworkQueryableExtensions.AnyAsync(db.Users)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReorderedNamedStaticEfExtensionSyntax_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:EntityFrameworkQueryableExtensions.ToListAsync(cancellationToken: default, source: db.Users)|},
                {|#1:EntityFrameworkQueryableExtensions.AnyAsync(cancellationToken: default, source: db.Users)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SequentialAwaits_OnSameContext_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await db.Users.ToListAsync();
            await db.Users.AnyAsync();
            await db.SaveChangesAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AwaitedTaskLocal_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            await first.ConfigureAwait(false);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentContexts_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext first, AppDbContext second)
        {
            await Task.WhenAll(
                first.Users.ToListAsync(),
                second.Users.ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryConstructionAndAsAsyncEnumerable_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public bool Active { get; set; } }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public Task Run(AppDbContext db)
        {
            var query = db.Users.Where(user => user.Active);
            var stream = query.AsAsyncEnumerable();
            return Task.CompletedTask;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomAsyncLookalike_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public static class CustomExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(new List<T>());
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                CustomExtensions.ToListAsync(db.Users),
                CustomExtensions.ToListAsync(db.Users));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
