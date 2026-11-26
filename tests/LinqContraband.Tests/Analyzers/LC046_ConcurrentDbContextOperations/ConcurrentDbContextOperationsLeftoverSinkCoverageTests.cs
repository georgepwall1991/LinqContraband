using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

/// <summary>
/// Leftover LC046 <c>QueryAsyncSinkNames</c> and <c>ExecuteSqlAsync</c> the shipped
/// <c>ToListAsync</c>/<c>AnyAsync</c>/<c>ContainsAsync</c>/<c>ElementAt*</c>/
/// <c>ToDictionaryAsync</c>/<c>LoadAsync</c>/<c>ExecuteDeleteAsync</c> and
/// <c>ExecuteSqlRawAsync</c>/<c>ExecuteSqlInterpolatedAsync</c> fixtures do not isolate.
/// Dropping leftover <c>CountAsync</c> fails only that pin; the shipped <c>ToListAsync</c>
/// WhenAll fixture stays green. The same holds for the other leftover query terminals,
/// leftover <c>ExecuteUpdateAsync</c>, and leftover <c>ExecuteSqlAsync</c>.
/// A lookalike leftover name outside <c>Microsoft.EntityFrameworkCore</c> stays quiet.
/// Field/property task stores, assignment-held uninvoked lambdas, and the other
/// documented LC046 FNs stay unpinned.
/// </summary>
public sealed class ConcurrentDbContextOperationsLeftoverSinkCoverageTests
{
    private const string LeftoverSinks = @"
namespace Microsoft.EntityFrameworkCore
{
    public static partial class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<bool> AllAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(true);

        public static System.Threading.Tasks.Task<double> AverageAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0d);

        public static System.Threading.Tasks.Task<int> CountAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0);

        public static System.Threading.Tasks.Task<TEntity> FirstAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task<TEntity> FirstOrDefaultAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task ForEachAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Action<TEntity> action,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.CompletedTask;

        public static System.Threading.Tasks.Task<TEntity> LastAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task<TEntity> LastOrDefaultAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task<long> LongCountAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0L);

        public static System.Threading.Tasks.Task<int> MaxAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0);

        public static System.Threading.Tasks.Task<int> MinAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0);

        public static System.Threading.Tasks.Task<TEntity> SingleAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task<TEntity> SingleOrDefaultAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(default(TEntity));

        public static System.Threading.Tasks.Task<int> SumAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0);

        public static System.Threading.Tasks.Task<TEntity[]> ToArrayAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(System.Array.Empty<TEntity>());

        public static System.Threading.Tasks.Task<System.Collections.Generic.HashSet<TEntity>> ToHashSetAsync<TEntity>(
            this System.Linq.IQueryable<TEntity> source,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.HashSet<TEntity>());
    }

    public static partial class RelationalDatabaseFacadeExtensions
    {
        public static System.Threading.Tasks.Task<int> ExecuteSqlAsync(
            this Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database,
            System.FormattableString sql,
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(0);
    }
}
";

    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult OverlapOnDb() =>
        VerifyCS.Diagnostic().WithLocation(1).WithLocation(0).WithArguments("db");

    private static string WrapQuery(string call) =>
        @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + ConcurrentDbContextOperationsTests.EfMock + LeftoverSinks + @"
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
                {|#0:db.Users." + call + @"|},
                {|#1:db.Users." + call + @"|});
        }
    }
}";

    private static string WrapDatabase(string call) =>
        @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + ConcurrentDbContextOperationsTests.EfMock + LeftoverSinks + @"
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
                {|#0:db.Database." + call + @"|},
                {|#1:db.Database." + call + @"|});
        }
    }
}";

    [Fact]
    public async Task AllAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("AllAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task AverageAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("AverageAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task CountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("CountAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task ExecuteUpdateAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("ExecuteUpdateAsync(new object())"), OverlapOnDb());
    }

    [Fact]
    public async Task FirstAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("FirstAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("FirstOrDefaultAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task ForEachAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("ForEachAsync(_ => { })"), OverlapOnDb());
    }

    [Fact]
    public async Task LastAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("LastAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task LastOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("LastOrDefaultAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task LongCountAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("LongCountAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task MaxAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("MaxAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task MinAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("MinAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task SingleAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("SingleAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("SingleOrDefaultAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task SumAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("SumAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task ToArrayAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("ToArrayAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task ToHashSetAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapQuery("ToHashSetAsync()"), OverlapOnDb());
    }

    [Fact]
    public async Task ExecuteSqlAsync_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            WrapDatabase(@"ExecuteSqlAsync($""SELECT {1}"")"),
            OverlapOnDb());
    }

    [Fact]
    public async Task LookalikeCountAsync_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + ConcurrentDbContextOperationsTests.EfMock + LeftoverSinks + @"
namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static Task<int> CountAsync<TEntity>(this System.Linq.IQueryable<TEntity> source) =>
            Task.FromResult(0);
    }
}

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
                OtherOrm.QueryExtensions.CountAsync(db.Users),
                OtherOrm.QueryExtensions.CountAsync(db.Users));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeExecuteSqlAsync_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + ConcurrentDbContextOperationsTests.EfMock + LeftoverSinks + @"
namespace OtherOrm
{
    public static class DatabaseExtensions
    {
        public static Task<int> ExecuteSqlAsync(
            this Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database,
            System.FormattableString sql) =>
            Task.FromResult(0);
    }
}

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
                OtherOrm.DatabaseExtensions.ExecuteSqlAsync(db.Database, $""SELECT {1}""),
                OtherOrm.DatabaseExtensions.ExecuteSqlAsync(db.Database, $""SELECT {2}""));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
