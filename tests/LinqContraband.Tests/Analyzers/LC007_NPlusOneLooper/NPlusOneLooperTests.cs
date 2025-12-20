using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

public class NPlusOneLooperTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(T entity) where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        
        public T Find(params object[] keyValues) => null;
    }

    public static class AsyncEnumerableExtensions
    {
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    public class EntityEntry<T> where T : class
    {
        public ReferenceEntry Reference(string name) => null;
        public CollectionEntry Collection(string name) => null;
    }
    public class ReferenceEntry { public void Load() { } }
    public class CollectionEntry { public void Load() { } }
}

namespace TestNamespace
{
    public class User { public int Id { get; set; } }
    
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}";

    [Fact]
    public async Task TestCrime_ForeachLoop_WithExplicitLoading_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var users = new List<User>();

        foreach (var user in users)
        {
            // Crime: Reference().Load() inside loop
            db.Entry(user).Reference(""abc"").Load();
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(20, 13)
            .WithArguments("Reference");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ForeachLoop_WithCollectionLoading_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var users = new List<User>();

        foreach (var user in users)
        {
            // Crime: Collection().Load() inside loop
            db.Entry(user).Collection(""abc"").Load();
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(20, 13)
            .WithArguments("Collection");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ForeachLoop_WithToList_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var ids = new List<int> { 1, 2, 3 };

        foreach (var id in ids)
        {
            // Crime: Query inside loop
            var user = db.Users.AsQueryable().Where(u => u.Id == id).ToList();
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(20, 24)
            .WithArguments("ToList");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ForLoop_WithFind_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        
        for (int i = 0; i < 10; i++)
        {
            // Crime: Find inside loop
            var user = db.Users.Find(i);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(19, 24)
            .WithArguments("Find");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_WhileLoop_WithCount_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        int i = 0;
        while (i < 10)
        {
            // Crime: Count inside loop
            var count = db.Users.Count();
            i++;
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(19, 25)
            .WithArguments("Count");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_QueryOutsideLoop_NoDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var ids = new List<int> { 1, 2, 3 };
        
        // Innocent: Query outside
        var users = db.Users.ToList();

        foreach (var user in users)
        {
            Console.WriteLine(user.Id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AwaitForeach_WithCount_TriggersDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    public async Task Run()
    {
        var db = new MyDbContext();
        await foreach (var user in db.Users.AsAsyncEnumerable())
        {
            var count = db.Users.Count();
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007")
            .WithLocation(17, 25)
            .WithArguments("Count");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_DeferredExecution_InsideLoop_NoDiagnostic()
    {
        var test = Usings + @"
using TestNamespace;
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var ids = new List<int> { 1, 2, 3 };

        foreach (var id in ids)
        {
            // Innocent: Just building query, not executing (yet)
            var query = db.Users.Where(u => u.Id == id); 
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
