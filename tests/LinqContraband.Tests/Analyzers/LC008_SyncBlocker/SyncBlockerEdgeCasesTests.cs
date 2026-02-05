using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC008_SyncBlocker;

public class SyncBlockerEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;

        public T Find(params object[] keyValues) => null;
        public Task<T> FindAsync(params object[] keyValues) => Task.FromResult<T>(null);
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
        public static Task<int> CountAsync<T>(this IQueryable<T> source) => Task.FromResult(0);
        public static Task<T> FirstAsync<T>(this IQueryable<T> source) => Task.FromResult<T>(default);
    }
}

namespace TestNamespace
{
    public class User { public int Id { get; set; } }

    public class MyDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }
}";

    [Fact]
    public async Task TestCrime_SyncCallInsideAsyncLambdaInsideAsyncMethod_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        Func<Task> action = async () =>
        {
            // Sync call inside async lambda inside async method
            var users = db.Users.ToList();
            await Task.Delay(1);
        };
        await action();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC008")
            .WithSpan(17, 25, 17, 42)
            .WithArguments("ToList", "ToListAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_SyncCallInsideNonAsyncLambdaInsideAsyncMethod_ShouldTrigger()
    {
        // The analyzer walks up to find async context even through non-async lambdas
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        Action action = () =>
        {
            // Sync call inside non-async lambda but enclosing method is async
            var users = db.Users.ToList();
        };
        action();
        await Task.Delay(1);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC008")
            .WithSpan(17, 25, 17, 42)
            .WithArguments("ToList", "ToListAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ValueTaskReturnType_WithSyncCall_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    async ValueTask<int> GetCount()
    {
        var db = new MyDbContext();
        return db.Users.Count();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC008")
            .WithSpan(14, 16, 14, 32)
            .WithArguments("Count", "CountAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_AsyncMethodWithFind_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    async Task<User> GetUser()
    {
        var db = new MyDbContext();
        return db.Users.Find(1);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC008")
            .WithSpan(14, 16, 14, 32)
            .WithArguments("Find", "FindAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_SyncMethodWithSyncCalls_NoDiagnostic()
    {
        // Sync call inside a non-async method is fine
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var users = db.Users.ToList();
        var count = db.Users.Count();
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AsyncMethod_MultipleSyncCalls_ShouldTriggerMultiple()
    {
        var test = Usings + @"
class Program
{
    async Task DoWork()
    {
        var db = new MyDbContext();
        var users = db.Users.ToList();
        db.SaveChanges();
        await Task.Delay(1);
    }
}
" + MockNamespace;

        var expected1 = VerifyCS.Diagnostic("LC008")
            .WithSpan(14, 21, 14, 38)
            .WithArguments("ToList", "ToListAsync");

        var expected2 = VerifyCS.Diagnostic("LC008")
            .WithSpan(15, 9, 15, 25)
            .WithArguments("SaveChanges", "SaveChangesAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task TestInnocent_SyncLambdaInSyncMethod_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        Action action = () =>
        {
            var users = db.Users.ToList();
        };
        action();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
