using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC041_SingleEntityScalarProjection;

/// <summary>
/// Leftover LC041 <c>QuerySteps</c>, <c>Set</c>, terminal, and receiver arms the original
/// 29 analyzer/fixer cases do not isolate. Dropping a named query step from
/// <c>QuerySteps</c> FNs only that pin because <c>TryGetEntityType</c> stops at the
/// unknown operator; <c>users.FirstOrDefault(x => x.IsActive)</c> stays green.
/// Dropping <c>OrderBy</c> also FNs the <c>ThenBy</c> pins, because
/// <c>ThenBy</c> sits after <c>OrderBy</c>. An unreduced static
/// <c>First</c> is the <c>GetInvocationReceiver</c> argument-0 arm; fluent
/// terminals keep using <c>Instance</c>.
/// </summary>
public sealed class SingleEntityScalarProjectionCoverageTests
{
    private const string EfCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() => new DbSet<TEntity>();
    }

    public class DbSet<TEntity>
    {
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static DbSet<TSource> Where<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => source;
        public static DbSet<TSource> OrderBy<TSource, TKey>(this DbSet<TSource> source, Expression<Func<TSource, TKey>> keySelector) => source;
        public static DbSet<TSource> OrderByDescending<TSource, TKey>(this DbSet<TSource> source, Expression<Func<TSource, TKey>> keySelector) => source;
        public static DbSet<TSource> ThenBy<TSource, TKey>(this DbSet<TSource> source, Expression<Func<TSource, TKey>> keySelector) => source;
        public static DbSet<TSource> ThenByDescending<TSource, TKey>(this DbSet<TSource> source, Expression<Func<TSource, TKey>> keySelector) => source;
        public static DbSet<TSource> Skip<TSource>(this DbSet<TSource> source, int count) => source;
        public static DbSet<TSource> Take<TSource>(this DbSet<TSource> source, int count) => source;
        public static DbSet<TSource> Distinct<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> Include<TSource, TProperty>(this DbSet<TSource> source, Expression<Func<TSource, TProperty>> navigation) => source;
        public static DbSet<TSource> ThenInclude<TSource, TProperty>(this DbSet<TSource> source, Expression<Func<TSource, TProperty>> navigation) => source;
        public static DbSet<TSource> IgnoreQueryFilters<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> AsSplitQuery<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> AsSingleQuery<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> AsTracking<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> AsNoTracking<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> AsNoTrackingWithIdentityResolution<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> IgnoreAutoIncludes<TSource>(this DbSet<TSource> source) => source;
        public static DbSet<TSource> TagWith<TSource>(this DbSet<TSource> source, string tag) => source;
        public static DbSet<TSource> TagWithCallSite<TSource>(this DbSet<TSource> source, [System.Runtime.CompilerServices.CallerFilePath] string filePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) => source;
        public static TSource First<TSource>(this DbSet<TSource> source) => default(TSource);
        public static TSource First<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static TSource FirstOrDefault<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static TSource SingleOrDefault<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
    }
}
";

    private static string Wrap(string statement)
    {
        return "using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public Customer Customer { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
        public ICollection<Order> Orders { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users, DbContext db)
        {
            " + statement + @"
        }
    }
}";
    }

    private static string WrapAsync(string statement)
    {
        return "using Microsoft.EntityFrameworkCore;" + @"
using System.Threading.Tasks;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public async Task Run(DbSet<User> users)
        {
            " + statement + @"
        }
    }
}";
    }

    [Fact]
    public async Task UnreducedStaticFirst_Reports()
    {
        // GetInvocationReceiver reads Arguments[0] when Instance is null.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:EntityFrameworkQueryableExtensions.First(users, x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task Set_ThenFirst_Reports()
    {
        // TryGetEntityType's Set arm; a DbSet parameter never reaches it.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:db.Set<User>().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task OrderBy_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.OrderBy(x => x.Name).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task OrderByDescending_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.OrderByDescending(x => x.Name).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task ThenBy_ThenFirst_Reports()
    {
        // ThenBy sits after OrderBy; dropping OrderBy from QuerySteps FNs this too.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.OrderBy(x => x.Id).ThenBy(x => x.Name).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task ThenByDescending_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.OrderBy(x => x.Id).ThenByDescending(x => x.Name).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task Skip_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.Skip(1).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task Take_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.Take(10).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task Distinct_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.Distinct().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task Include_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.Include(x => x.Orders).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task ThenInclude_ThenFirst_Reports()
    {
        // ThenInclude is a QuerySteps name of its own; placing it on DbSet isolates
        // that arm without depending on Include remaining in the set.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.ThenInclude(x => x.Orders).First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task IgnoreQueryFilters_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.IgnoreQueryFilters().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task AsSplitQuery_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.AsSplitQuery().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task AsSingleQuery_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.AsSingleQuery().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task AsTracking_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.AsTracking().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task AsNoTracking_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.AsNoTracking().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.AsNoTrackingWithIdentityResolution().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task IgnoreAutoIncludes_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.IgnoreAutoIncludes().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task TagWith_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.TagWith(""active"").First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task TagWithCallSite_ThenFirst_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.TagWithCallSite().First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSinglePropertyUsage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapAsync(
            @"var user = await {|LC041:users.FirstOrDefaultAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task SingleOrDefault_WithSinglePropertyUsage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(
            @"var user = {|LC041:users.SingleOrDefault(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithSinglePropertyUsage_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapAsync(
            @"var user = await {|LC041:users.SingleOrDefaultAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);"));
    }
}
