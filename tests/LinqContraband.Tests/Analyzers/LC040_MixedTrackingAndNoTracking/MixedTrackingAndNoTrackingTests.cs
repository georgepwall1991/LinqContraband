using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking.MixedTrackingAndNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC040_MixedTrackingAndNoTracking;

public class MixedTrackingAndNoTrackingTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsNoTrackingWithIdentityResolution<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    private const string Types = @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    [Fact]
    public async Task TrackedAndNoTrackingOnSameContext_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.ToList();
        var second = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoTrackingThenTrackedOnSameContext_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.AsNoTracking().ToList();
        var second = {|LC040:db.Users.ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_CountsAsNoTracking()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.ToList();
        var second = {|LC040:db.Users.AsNoTrackingWithIdentityResolution().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomAsNoTrackingExtension_DoesNotCountAsEfNoTracking()
    {
        var test = EFCoreMock + Types + @"

namespace CustomQueryExtensions
{
    public static class QueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.ToList();
        var second = CustomQueryExtensions.QueryableExtensions.AsNoTracking(db.Users).ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameTrackingMode_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.ToList();
        var second = db.Users.FirstOrDefault();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentContextInstances_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db1 = new TestApp.AppDbContext();
        var db2 = new TestApp.AppDbContext();
        var first = db1.Users.ToList();
        var second = db2.Users.AsNoTracking().ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalAliasToDbSet_ResolvesContextAndTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var users = db.Users;
        var first = users.ToList();
        var second = {|LC040:users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
