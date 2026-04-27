using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

public class NPlusOneLooperTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Query;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Query;

    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(T entity) where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;

        public T Find(params object[] keyValues) => null;
        public Task<T> FindAsync(params object[] keyValues) => Task.FromResult<T>(null);
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
        public static Task<int> CountAsync<T>(this IQueryable<T> source) => Task.FromResult(0);
        public static Task<bool> AnyAsync<T>(this IQueryable<T> source) => Task.FromResult(false);
        public static int ExecuteDelete<T>(this IQueryable<T> source) => 0;
        public static Task<int> ExecuteDeleteAsync<T>(this IQueryable<T> source) => Task.FromResult(0);
        public static int ExecuteUpdate<T>(this IQueryable<T> source, Expression<Func<T, T>> updateExpression) => 0;
        public static Task<int> ExecuteUpdateAsync<T>(this IQueryable<T> source, Expression<Func<T, T>> updateExpression) => Task.FromResult(0);

        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            => null;
    }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    public class EntityEntry<TEntity> where TEntity : class
    {
        public ReferenceEntry Reference(string name) => null;
        public CollectionEntry Collection(string name) => null;

        public ReferenceEntry<TEntity, TProperty> Reference<TProperty>(Expression<Func<TEntity, TProperty>> navigationPropertyPath) => null;
        public CollectionEntry<TEntity, TProperty> Collection<TProperty>(Expression<Func<TEntity, IEnumerable<TProperty>>> navigationPropertyPath) => null;
    }

    public class ReferenceEntry
    {
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
    }

    public class CollectionEntry
    {
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
        public IQueryable<T> Query<T>() => null;
    }

    public class ReferenceEntry<TEntity, TProperty>
    {
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
    }

    public class CollectionEntry<TEntity, TProperty>
    {
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
        public IQueryable<TProperty> Query() => null;
    }
}

namespace TestNamespace
{
    public class User
    {
        public int Id { get; set; }
        public List<Order> Orders { get; set; }
        public Profile Profile { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
    }

    public class Profile
    {
        public int Id { get; set; }
    }

    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
        public IQueryable<User> AmbiguousUsers { get; set; }
    }
}";

    [Fact]
    public async Task Find_InForLoop_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();

        for (var i = 0; i < 10; i++)
        {
            var user = {|#0:db.Users.Find(i)|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Find");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FindAsync_InForeachLoop_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var ids = new List<int> { 1, 2, 3 };

        foreach (var id in ids)
        {
            var user = await {|#0:db.Users.FindAsync(id)|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("FindAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExplicitLoad_StringAccessor_StillTriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            {|#0:db.Entry(user).Collection(""Orders"").Load()|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Load");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TypedNavigationQuery_Count_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            var count = {|#0:db.Entry(user).Collection(u => u.Orders).Query().Count()|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Count");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSetQueryMaterializer_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            var users = {|#0:db.Set<User>().Where(u => u.Id == id).ToList()|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("ToList");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SingleAssignmentLocalEfQuery_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var query = db.Users.Where(u => u.Id > 0);

        foreach (var id in new[] { 1, 2, 3 })
        {
            var any = {|#0:query.Any(u => u.Id == id)|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Any");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteDelete_InWhileLoop_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var i = 0;
        while (i < 5)
        {
            {|#0:db.Users.Where(u => u.Id == i).ExecuteDelete()|};
            i++;
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("ExecuteDelete");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Count_InAwaitForeach_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    async Task Run()
    {
        var db = new MyDbContext();
        await foreach (var user in db.Users.AsAsyncEnumerable())
        {
            var count = {|#0:db.Users.Count()|};
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Count");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InMemoryAsQueryable_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var users = new List<User>().AsQueryable();

        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = users.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableParameter_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    void Main(IQueryable<User> query)
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = query.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableProperty_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    private readonly MyDbContext _db = new MyDbContext();

    void Main()
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = _db.AmbiguousUsers.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiAssignedLocal_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IQueryable<User> query = db.Users;
        query = db.Users.Where(u => u.Id > 10);

        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = query.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryConstructionOnly_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            var query = db.Users.Where(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedListAggregates_InLoop_AreIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var users = db.Users.ToList();

        foreach (var id in new[] { 1, 2, 3 })
        {
            var single = users.Single(u => u.Id == id);
            var sum = users.Sum(u => u.Id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AccessorsWithoutExecution_AreIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            db.Entry(user).Reference(u => u.Profile);
            db.Entry(user).Collection(u => u.Orders);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InvocationInsideLambdaDeclaredInLoop_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            Func<int> countUsers = () => db.Users.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
