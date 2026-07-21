using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsTests
{
    internal const string EfMock = @"
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

    public static class RelationalDatabaseFacadeExtensions
    {
        public static Task<int> ExecuteSqlRawAsync(
            this Infrastructure.DatabaseFacade database,
            string sql,
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
