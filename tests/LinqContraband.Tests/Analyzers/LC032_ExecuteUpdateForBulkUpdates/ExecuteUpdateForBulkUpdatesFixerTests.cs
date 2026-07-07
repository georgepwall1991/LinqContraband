using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public partial class ExecuteUpdateForBulkUpdatesFixerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestApp;
";

    // The analyzer-only mock can type ExecuteUpdate's argument as `object`, but the FIXER
    // tests must compile the generated `ExecuteUpdate(setters => setters.SetProperty(...))`
    // call, so this mock models a real SetPropertyCalls<T> builder.
    private const string EFCoreMockWithExecuteUpdate = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
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

    public class SetPropertyCalls<TSource>
    {
        public SetPropertyCalls<TSource> SetProperty<TProperty>(Func<TSource, TProperty> propertyExpression, Func<TSource, TProperty> valueExpression) => this;
        public SetPropertyCalls<TSource> SetProperty<TProperty>(Func<TSource, TProperty> propertyExpression, TProperty valueExpression) => this;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>> setPropertyCalls) => 0;
        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source, Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>> setPropertyCalls, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
";

    // Same as above but WITHOUT ExecuteUpdateAsync: lets us assert the fixer declines (rather
    // than inject a blocking sync-over-async call) inside an async context.
    private const string EFCoreMockWithExecuteUpdateSyncOnly = @"
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

    public class SetPropertyCalls<TSource>
    {
        public SetPropertyCalls<TSource> SetProperty<TProperty>(Func<TSource, TProperty> propertyExpression, Func<TSource, TProperty> valueExpression) => this;
        public SetPropertyCalls<TSource> SetProperty<TProperty>(Func<TSource, TProperty> propertyExpression, TProperty valueExpression) => this;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>> setPropertyCalls) => 0;
    }
}
";

    private const string TestTypes = @"
namespace TestApp
{
    public enum UserStatus
    {
        Active,
        Archived
    }

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
        public UserStatus Status { get; set; }
        public Profile Profile { get; set; }
        public List<Order> Orders { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    private const string WarningComment =
        "// Warning: ExecuteUpdate runs immediately and bypasses change tracking and entity callbacks.";

    private static string WithExecuteUpdate(string program) =>
        Usings + EFCoreMockWithExecuteUpdate + TestTypes + program;

    private static string WithSyncOnly(string program) =>
        Usings + EFCoreMockWithExecuteUpdateSyncOnly + TestTypes + program;

    [Fact]
    public async Task Fixer_ConstantRhs_InlineQuery_RewritesToExecuteUpdate()
    {
        var test = WithExecuteUpdate(@"
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
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_SelfMemberArithmeticRhs_TransplantsVerbatim()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.LoginCount = user.LoginCount + 1;
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.LoginCount, user => user.LoginCount + 1));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_MultipleProperties_ChainsSetProperty()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
            user.LoginCount = user.LoginCount + 1;
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived"").SetProperty(user => user.LoginCount, user => user.LoginCount + 1));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_AsyncContext_UsesExecuteUpdateAsyncAndAwait()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        await db.Users.Where(u => u.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        await db.SaveChangesAsync();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_AsyncWithCancellationToken_PropagatesToken()
    {
        // The token on the awaited SaveChangesAsync must move onto ExecuteUpdateAsync (now the
        // actual database call), not be silently dropped.
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run(CancellationToken token)
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync(token);
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    async Task Run(CancellationToken token)
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        await db.Users.Where(u => u.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Name, user => ""Archived""), token);
        await db.SaveChangesAsync(token);
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_EnumAssignment_Rewrites()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Status = UserStatus.Archived;
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Status, user => UserStatus.Archived));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_CapturedVariableRhs_TransplantsVerbatim()
    {
        // The analyzer permits an RHS that references an outer parameter/local (EF parameterizes
        // the captured value); the fixer transplants it verbatim into the value lambda.
        var test = WithExecuteUpdate(@"
class Program
{
    void Run(string archivedName)
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = archivedName;
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run(string archivedName)
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => archivedName));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_DuplicatePropertyAssignment_KeepsLastWrite()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""First"";
            user.Name = ""Second"";
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Second""));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_RewritesAllQualifyingBulkUpdateLoops()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|#0:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();

        {|#1:foreach (var user in db.Users.Where(u => !u.IsActive))
        {
            user.LoginCount = user.LoginCount + 1;
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();

        " + WarningComment + @"
        db.Users.Where(u => !u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.LoginCount, user => user.LoginCount + 1));
        db.SaveChanges();
    }
}");

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseExecuteUpdate"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC032", DiagnosticSeverity.Info)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC032", DiagnosticSeverity.Info)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
