using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public class ExecuteUpdateForBulkUpdatesTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestApp;
";

    private const string EFCoreMockWithExecuteUpdate = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, object updates) => 0;
        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source, object updates) => Task.FromResult(0);
    }
}
";

    private const string EFCoreMockWithoutExecuteUpdate = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
    }
}
";

    private const string TestTypes = @"
namespace TestApp
{
    public class Profile
    {
        public string Name { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
        public int LoginCount { get; set; }
        public Profile Profile { get; set; }
        public List<Order> Orders { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    [Fact]
    public async Task DirectQueryForeach_WithScalarAssignment_AndSaveChanges_Triggers()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedLocalForeach_WithSaveChangesAsync_Triggers()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        var users = await db.Users.Where(u => u.IsActive).ToListAsync();
        {|LC032:foreach (var user in users)
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryLocal_WithMultipleScalarAssignments_Triggers()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        var users = db.Users.Where(u => u.IsActive);
        {|LC032:foreach (var user in users)
        {
            user.Name = ""Archived"";
            user.LoginCount = user.LoginCount + 1;
        }|}
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PlainListSource_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run(List<User> users)
    {
        using var db = new AppDbContext();
        foreach (var user in users)
        {
            user.Name = ""Archived"";
        }
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentDbContextForSave_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var readDb = new AppDbContext();
        using var writeDb = new AppDbContext();
        foreach (var user in readDb.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }
        writeDb.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MissingSaveChanges_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SideEffectfulLoopBody_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Touch(User user) { }

    void Run()
    {
        using var db = new AppDbContext();
        foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
            Touch(user);
        }
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BranchingLoopBody_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        foreach (var user in db.Users.Where(u => u.IsActive))
        {
            if (user.IsActive)
            {
                user.Name = ""Archived"";
            }
        }
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NavigationMutation_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Profile.Name = ""Archived"";
        }
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MissingExecuteUpdateSupport_DoesNotTrigger()
    {
        var test = Usings + EFCoreMockWithoutExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }
        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
