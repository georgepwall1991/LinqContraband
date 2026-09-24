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
        public DatabaseFacade Database { get; } = new DatabaseFacade();
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DatabaseFacade
    {
        public IDbContextTransaction BeginTransaction() => null;
    }

    public interface IDbContextTransaction : System.IDisposable
    {
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
        public static IQueryable<TSource> AsSplitQuery<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
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
    public async Task TernaryTrackedAndNoTrackingArms_DoNotTrigger()
    {
        // The two ternary arms are mutually exclusive — only one materializes at runtime — so no
        // scope actually mixes tracking modes (matches the if/else contract).
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool readOnly)
    {
        var list = readOnly
            ? db.Users.AsNoTracking().ToList()
            : db.Users.ToList();
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
    public async Task TransparentEfQueryOptions_PreserveTrackingModeEvidence()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Users.AsSplitQuery().TagWith(""tracked path"").ToList();
        var second = {|LC040:db.Users.AsNoTracking().AsSplitQuery().TagWith(""read-only path"").ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSetTrackedQueryAndNoTrackingPropertyQuery_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var first = db.Set<TestApp.User>().ToList();
        var second = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExplicitTransaction_DoesNotHideMixedTrackingModes()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        using var transaction = db.Database.BeginTransaction();

        var tracked = db.Users.ToList();
        var readOnly = {|LC040:db.Users.AsNoTracking().ToList()|};
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

    [Fact]
    public async Task SelfReassignedLocalQuery_ResolvesThroughEachStepAndTriggers()
    {
        // `query = query.Where(...)` reads the local inside its own assignment; resolving that read
        // to the assignment itself used to loop forever and hang the build.
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var query = db.Users.Where(u => u.Id > 0);
        query = query.Where(u => u.Name != null);
        query = query.OrderBy(u => u.Id);
        var first = query.ToList();
        var second = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelfReassignedLocalQuery_KeepsEarlierAsNoTracking()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        IQueryable<TestApp.User> query = db.Users;
        query = query.AsNoTracking();
        query = query.Where(u => u.Id > 0);
        var first = query.ToList();
        var second = db.Users.AsNoTracking().FirstOrDefault();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelfReassignedNoTrackingQueryThenTrackedRead_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        IQueryable<TestApp.User> query = db.Users;
        query = query.AsNoTracking();
        query = query.Where(u => u.Id > 0);
        var first = query.ToList();
        var second = {|LC040:db.Users.First()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelfReassignedQueryFromOutVariable_StaysQuiet()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        if (!TryGetQuery(db, out var query))
            return;

        query = query.Where(u => u.Id > 0);
        var first = query.ToList();
        var second = db.Users.AsNoTracking().ToList();
    }

    static bool TryGetQuery(TestApp.AppDbContext db, out IQueryable<TestApp.User> query)
    {
        query = db.Users;
        return true;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyReturnTrackedBranch_DoesNotTrigger()
    {
        // Kavita's `GetAllUsersAsync(bool track)`: the no-tracking read only runs when the
        // tracked branch did not, because that branch returns.
        var test = EFCoreMock + Types + @"

class Program
{
    System.Collections.Generic.List<TestApp.User> Run(TestApp.AppDbContext db, bool track)
    {
        var query = db.Users.Where(u => u.Id > 0);

        if (track)
        {
            return query.ToList();
        }

        return query.AsNoTracking().ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyThrowAfterTrackedRead_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    System.Collections.Generic.List<TestApp.User> Run(TestApp.AppDbContext db, bool strict)
    {
        if (strict)
        {
            var tracked = db.Users.ToList();
            throw new System.InvalidOperationException(tracked.Count.ToString());
        }

        return db.Users.AsNoTracking().ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BranchWithoutExitBeforeLaterRead_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool track)
    {
        if (track)
        {
            var tracked = db.Users.ToList();
        }

        var detached = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnInsideLambdaBranch_DoesNotHideLaterRead()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db, bool track)
    {
        var tracked = db.Users.ToList();
        System.Func<int> count = () =>
        {
            if (track)
                return 1;

            return 0;
        };

        var detached = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperProjectingToDto_DoesNotTrigger()
    {
        // Kavita projects with AutoMapper's ProjectTo<Dto>(); EF Core does not track DTOs.
        var test = EFCoreMock + Types + @"

class UserDto
{
    public int Id { get; set; }
}

static class Projections
{
    public static IQueryable<UserDto> ToDtos(this IQueryable<TestApp.User> users) =>
        users.Select(u => new UserDto { Id = u.Id });

    public static IQueryable<int> SelectIds(this IQueryable<TestApp.User> users) =>
        users.Select(u => u.Id);
}

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var dtos = db.Users.ToDtos().ToList();
        var ids = db.Users.SelectIds().ToList();
        var detached = db.Users.AsNoTracking().ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntityPreservingHelper_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

static class Filters
{
    public static IQueryable<TestApp.User> Named(this IQueryable<TestApp.User> users) =>
        users.Where(u => u.Name != null);
}

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var tracked = db.Users.Named().ToList();
        var detached = {|LC040:db.Users.AsNoTracking().ToList()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
