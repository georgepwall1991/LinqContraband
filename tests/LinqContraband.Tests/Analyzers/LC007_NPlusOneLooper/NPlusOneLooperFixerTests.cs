using Microsoft.CodeAnalysis.Testing;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer,
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperFixer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

public class NPlusOneLooperFixerTests
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
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;

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
    }
}";

    [Fact]
    public async Task CollectionLoad_InForeach_AddsIncludeAndRemovesLoad()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            {|#0:db.Entry(user).Collection(u => u.Orders).Load()|};
            Console.WriteLine(user.Id);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.Include(u => u.Orders).ToList())
        {
            Console.WriteLine(user.Id);
        }
    }
}
" + MockNamespace;

        var expected = VerifyFix.Diagnostic("LC007").WithLocation(0).WithArguments("Load");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ReferenceLoadAsync_InAwaitForeach_AddsIncludeBeforeAsyncEnumeration()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var user in db.Users.AsAsyncEnumerable())
        {
            await {|#0:db.Entry(user).Reference(u => u.Profile).LoadAsync()|};
            Console.WriteLine(user.Id);
        }
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        await foreach (var user in db.Users.Include(u => u.Profile).AsAsyncEnumerable())
        {
            Console.WriteLine(user.Id);
        }
    }
}
" + MockNamespace;

        var expected = VerifyFix.Diagnostic("LC007").WithLocation(0).WithArguments("LoadAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task StringBasedExplicitLoad_DoesNotRegisterFixer()
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

        var expected = VerifyFix.Diagnostic("LC007").WithLocation(0).WithArguments("Load");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ConditionalExplicitLoad_DoesNotRegisterFixer()
    {
        var test = Usings + @"
class Program
{
    void Main(bool includeOrders)
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            if (includeOrders)
            {
                {|#0:db.Entry(user).Collection(u => u.Orders).Load()|};
            }
        }
    }
}
" + MockNamespace;

        var expected = VerifyFix.Diagnostic("LC007").WithLocation(0).WithArguments("Load");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }
}
