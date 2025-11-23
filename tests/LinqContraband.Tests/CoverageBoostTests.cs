using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS_LC006 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC006_CartesianExplosion.CartesianExplosionAnalyzer>;
using VerifyCS_LC007 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;
using VerifyCS_LC008 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer>;
using VerifyCS_LC009 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests;

public class CoverageBoostTests
{
    private const string CommonUsings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockEfCore = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Add<T>(T entity) { }
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public T Find(params object[] keyValues) => null;
        public void Add(T entity) { }
    }

    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(this IQueryable<TEntity> source, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationPropertyPath) => null;
        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source, System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) => null;
        public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
    }
}

namespace TestNamespace
{
    public class User { public int Id { get; set; } public List<Order> Orders { get; set; } public List<Role> Roles { get; set; } }
    public class Order { public List<Item> Items { get; set; } }
    public class Item { }
    public class Role { }
    public class MyDbContext : DbContext { public DbSet<User> Users { get; set; } }
}
";

    [Fact]
    public async Task LC006_Cartesian_ThenIncludeCollection_Triggers()
    {
        var test = CommonUsings + MockEfCore + @"
class Program {
    class ArrayUser { public int[] Roles { get; set; } public string[] Tags { get; set; } }
    class ArrayCtx : DbContext { public DbSet<ArrayUser> ArrayUsers { get; set; } }

    void Main() {
        var db = new ArrayCtx();
        var query = db.ArrayUsers.Include(u => u.Roles).Include(u => u.Tags).ToList();
    }
}";
        // Corrected line: 59
        var expected = VerifyCS_LC006.Diagnostic("LC006")
            .WithSpan(59, 21, 59, 77)
            .WithArguments("string[]"); // Int32[] or string[]? The second one is string[]. "Tags".
            
        await VerifyCS_LC006.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LC007_NPlusOne_DoWhile_Triggers()
    {
        var test = CommonUsings + MockEfCore + @"
class Program {
    void Main() {
        var db = new MyDbContext();
        int i = 0;
        do {
            var u = db.Users.Find(i);
            i++;
        } while (i < 10);
    }
}";
        // Corrected line: 58
        var expected = VerifyCS_LC007.Diagnostic("LC007")
            .WithSpan(58, 21, 58, 37)
            .WithArguments("Find");
        await VerifyCS_LC007.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LC008_SyncBlocker_Find_InAsync_Triggers()
    {
        var test = CommonUsings + MockEfCore + @"
class Program {
    async Task Main() {
        var db = new MyDbContext();
        var u = db.Users.Find(1); // Sync Find
        await Task.Delay(1);
    }
}";
        // Corrected line: 56
        var expected = VerifyCS_LC008.Diagnostic("LC008")
            .WithSpan(56, 17, 56, 33)
            .WithArguments("Find", "FindAsync");
        await VerifyCS_LC008.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LC009_TrackingTax_Add_PreventsDiagnostic()
    {
        var test = CommonUsings + MockEfCore + @"
class Program {
    public List<User> ModifyUsers() {
        var db = new MyDbContext();
        db.Users.Add(new User()); // Side effect on DbSet!
        // Should NOT trigger LC009
        return db.Users.ToList();
    }
}";
        await VerifyCS_LC009.VerifyAnalyzerAsync(test);
    }
}