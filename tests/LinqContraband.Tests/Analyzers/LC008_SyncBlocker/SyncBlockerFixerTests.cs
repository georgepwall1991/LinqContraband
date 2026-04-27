using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer,
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC008_SyncBlocker;

public class SyncBlockerFixerTests
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
    public async Task FixCrime_ToList_ReplacedWithAwaitToListAsync()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        // Fix me
        var users = db.Users.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        // Fix me
        var users = await db.Users.ToListAsync();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        // Line 15
        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC008", DiagnosticSeverity.Warning)
            .WithSpan(15, 21, 15, 38)
            .WithArguments("ToList", "ToListAsync"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_SaveChanges_ReplacedWithAwaitSaveChangesAsync()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        db.SaveChanges();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        await db.SaveChangesAsync();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        // Line 14
        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC008", DiagnosticSeverity.Warning)
            .WithSpan(14, 9, 14, 25)
            .WithArguments("SaveChanges", "SaveChangesAsync"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InsideLetClause_QuerySubqueryDoesNotReport()
    {
        // The sync-looking call is part of the query expression and translates
        // as a SQL subquery instead of blocking the async method directly.
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var q = from u in db.Users
                let posts = db.Users.Where(x => x.Id == u.Id).ToList()
                select new { u, posts };
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = test
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InsideWhereClause_QuerySubqueryDoesNotReport()
    {
        // Sanity-check another translated query expression clause position.
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var q = from u in db.Users
                where db.Users.Where(x => x.Id == u.Id).ToList().Count > 0
                select u;
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = test
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InsideInitialFromClause_FixIsApplied()
    {
        // 'await' IS legal inside the collection expression of the initial
        // 'from' clause, so the fixer should still rewrite the call there.
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var q = from u in db.Users.ToList()
                select u;
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var q = from u in await db.Users.ToListAsync() select u;
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC008", DiagnosticSeverity.Warning)
            .WithSpan(14, 27, 14, 44)
            .WithArguments("ToList", "ToListAsync"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InsideNonAsyncLambda_DiagnosticReportedButNoFixApplied()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        Func<List<User>> getUsers = () => db.Users.ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = test
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC008", DiagnosticSeverity.Warning)
            .WithSpan(14, 43, 14, 60)
            .WithArguments("ToList", "ToListAsync"));

        await testObj.RunAsync();
    }
}
