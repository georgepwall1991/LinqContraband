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
    public async Task IfElseTrackedAndNoTrackingBranches_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool readOnly)
    {
        if (readOnly)
        {
            var first = db.Users.AsNoTracking().ToList();
        }
        else
        {
            var second = db.Users.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SwitchCaseTrackedAndNoTrackingBranches_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, int mode)
    {
        switch (mode)
        {
            case 1:
                var first = db.Users.AsNoTracking().ToList();
                break;
            case 2:
                var second = db.Users.ToList();
                break;
            default:
                var third = db.Users.AsNoTrackingWithIdentityResolution().ToList();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IndependentIfStatements_CanTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool first, bool second)
    {
        if (first)
        {
            var tracked = db.Users.ToList();
        }

        if (second)
        {
            var noTracking = {|LC040:db.Users.AsNoTracking().ToList()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LaterMaterializationComparesAgainstSwitchBranchRecords()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, int mode)
    {
        switch (mode)
        {
            case 1:
                var first = db.Users.AsNoTracking().ToList();
                break;
            case 2:
                var second = db.Users.ToList();
                break;
        }

        var later = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LaterMaterializationComparesAgainstNonExclusivePriorRecords()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool readOnly)
    {
        if (readOnly)
        {
            var first = db.Users.AsNoTracking().ToList();
        }
        else
        {
            var second = db.Users.ToList();
        }

        var later = {|LC040:db.Users.AsNoTracking().ToList()|};
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

    [Fact]
    public async Task ReassignedLocalQueryOnSameContext_ResolvesAssignmentBeforeUseAndTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        IQueryable<TestApp.User> users = db.Users;
        var first = users.ToList();

        users = db.Users.AsNoTracking();
        var second = {|LC040:users.ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedLocalQueryOnDifferentContexts_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db1, TestApp.AppDbContext db2)
    {
        IQueryable<TestApp.User> users = db1.Users;
        var first = users.ToList();

        users = db2.Users.AsNoTracking();
        var second = users.ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionallyReassignedLocalQuery_StaysConservative()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool readOnly)
    {
        IQueryable<TestApp.User> users = db.Users;
        var first = users.ToList();

        if (readOnly)
        {
            users = db.Users.AsNoTracking();
        }

        var second = users.ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
