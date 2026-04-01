using VerifyCS_LC008 =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer>;
using VerifyCS_LC009 =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;
using VerifyCS_LC010 =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;
using VerifyCS_LC025 =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;

namespace LinqContraband.Tests;

/// <summary>
/// Tests verifying that multiple analyzers interact correctly on the same code.
/// Each analyzer is run independently on shared test code to verify expected diagnostics
/// and confirm no conflicts between rules.
/// </summary>
public class CrossAnalyzerTests
{
    // Shared mock infrastructure covering types needed by LC008, LC009, LC010, and LC025
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
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
        public System.Threading.Tasks.Task<int> SaveChangesAsync() => System.Threading.Tasks.Task.FromResult(0);
        public void Add<T>(T entity) { }
        public void Update(object entity) { }
        public void Remove(object entity) { }
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public T Find(params object[] keyValues) => null;
        public System.Threading.Tasks.Task<T> FindAsync(params object[] keyValues) => System.Threading.Tasks.Task.FromResult<T>(null);
        public void Add(T entity) { }
        public void Update(T entity) { }
        public void Remove(T entity) { }
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) => source;
        public static System.Threading.Tasks.Task<System.Collections.Generic.List<T>> ToListAsync<T>(this IQueryable<T> source) => System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<T>());
        public static System.Threading.Tasks.Task<int> CountAsync<T>(this IQueryable<T> source) => System.Threading.Tasks.Task.FromResult(0);
    }
}

namespace TestNamespace
{
    public class Item { public int Id { get; set; } public string Name { get; set; } }

    public class MyDbContext : DbContext
    {
        public DbSet<Item> Items { get; set; }
    }
}";

    #region Test 1: LC010 + LC008 (SaveChanges in loop inside async method)

    /// <summary>
    /// SaveChanges() inside a foreach loop within an async method should trigger LC010.
    /// </summary>
    [Fact]
    public async Task LC010_SaveChangesInLoop_InAsyncMethod_Triggers()
    {
        // Usings: lines 1-7 (empty first line + 6 using lines + empty trailing line)
        // Line  8: (empty - start of inline code block)
        // Line  9: class Program
        // Line 10: {
        // Line 11:     async Task ProcessItems()
        // Line 12:     {
        // Line 13:         var db = new MyDbContext();
        // Line 14:         var items = new List<Item> { new Item(), new Item() };
        // Line 15:         (empty)
        // Line 16:         foreach (var item in items)
        // Line 17:         {
        // Line 18:             db.Items.Add(item);
        // Line 19:             db.SaveChanges();
        var test = Usings + @"
class Program
{
    async Task ProcessItems()
    {
        var db = new MyDbContext();
        var items = new List<Item> { new Item(), new Item() };

        foreach (var item in items)
        {
            db.Items.Add(item);
            db.SaveChanges();
        }
        await Task.CompletedTask;
    }
}" + MockEfCore;

        var expected = VerifyCS_LC010.Diagnostic("LC010")
            .WithSpan(19, 13, 19, 29)
            .WithArguments("SaveChanges");

        await VerifyCS_LC010.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// SaveChanges() inside a foreach loop within an async method should also trigger LC008
    /// (sync call in async context).
    /// </summary>
    [Fact]
    public async Task LC008_SyncSaveChangesInAsyncMethod_WithLoop_Triggers()
    {
        var test = Usings + @"
class Program
{
    async Task ProcessItems()
    {
        var db = new MyDbContext();
        var items = new List<Item> { new Item(), new Item() };

        foreach (var item in items)
        {
            db.Items.Add(item);
            db.SaveChanges();
        }
        await Task.CompletedTask;
    }
}" + MockEfCore;

        var expected = VerifyCS_LC008.Diagnostic("LC008")
            .WithSpan(19, 13, 19, 29)
            .WithArguments("SaveChanges", "SaveChangesAsync");

        await VerifyCS_LC008.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Test 2: LC009 + LC025 mutual exclusivity

    /// <summary>
    /// Code with AsNoTracking() that only reads should NOT trigger LC009
    /// (missing AsNoTracking) because AsNoTracking is already present.
    /// </summary>
    [Fact]
    public async Task LC009_WithAsNoTracking_ReadOnly_DoesNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<Item> GetItems()
    {
        var db = new MyDbContext();
        return db.Items.AsNoTracking().ToList();
    }
}" + MockEfCore;

        await VerifyCS_LC009.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Code without AsNoTracking() in a read-only method should trigger LC009.
    /// </summary>
    [Fact]
    public async Task LC009_WithoutAsNoTracking_ReadOnly_Triggers()
    {
        // Usings: lines 1-7
        // Line  8: (empty)
        // Line  9: class Program
        // Line 10: {
        // Line 11:     public List<Item> GetItems()
        // Line 12:     {
        // Line 13:         var db = new MyDbContext();
        // Line 14:         return db.Items.Where(i => i.Id > 0).ToList();
        var test = Usings + @"
class Program
{
    public List<Item> GetItems()
    {
        var db = new MyDbContext();
        return db.Items.Where(i => i.Id > 0).ToList();
    }
}" + MockEfCore;

        var expected = VerifyCS_LC009.Diagnostic("LC009")
            .WithSpan(14, 16, 14, 54)
            .WithArguments("GetItems");

        await VerifyCS_LC009.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Code without AsNoTracking() but calling SaveChanges should NOT trigger LC009
    /// because tracking is needed for updates.
    /// </summary>
    [Fact]
    public async Task LC009_WithUpdate_DoesNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public void UpdateItems()
    {
        var db = new MyDbContext();
        var items = db.Items.ToList();
        db.SaveChanges();
    }
}" + MockEfCore;

        await VerifyCS_LC009.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// LC025 should trigger when AsNoTracking() query result is passed to Update().
    /// Uses the markup syntax that LC025 tests use.
    /// </summary>
    [Fact]
    public async Task LC025_AsNoTracking_ThenUpdate_Triggers()
    {
        var test = @"using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
" + MockEfCore + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(DbSet<TestNamespace.Item> items)
        {
            var item = items.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (item != null)
            {
                items.Update({|LC025:item|});
            }
        }
    }
}";

        await VerifyCS_LC025.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// LC025 should NOT trigger when query does not use AsNoTracking().
    /// Tracked entities can be safely updated.
    /// </summary>
    [Fact]
    public async Task LC025_WithoutAsNoTracking_ThenUpdate_DoesNotTrigger()
    {
        var test = @"using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
" + MockEfCore + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(DbSet<TestNamespace.Item> items)
        {
            var item = items.FirstOrDefault(x => x.Id == 1);
            if (item != null)
            {
                items.Update(item);
            }
        }
    }
}";

        await VerifyCS_LC025.VerifyAnalyzerAsync(test);
    }

    #endregion

    #region Test 3: No duplicate diagnostics

    /// <summary>
    /// A single SaveChanges() in a loop should produce exactly one LC010 diagnostic,
    /// not duplicates.
    /// </summary>
    [Fact]
    public async Task LC010_SingleCallInLoop_ProducesExactlyOneDiagnostic()
    {
        // Usings: lines 1-7
        // Line  8: (empty)
        // Line  9: class Program
        // Line 10: {
        // Line 11:     void Main()
        // Line 12:     {
        // Line 13:         var db = new MyDbContext();
        // Line 14:         var items = new List<int> { 1, 2, 3 };
        // Line 15:         (empty)
        // Line 16:         foreach (var item in items)
        // Line 17:         {
        // Line 18:             db.SaveChanges();
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            db.SaveChanges();
        }
    }
}" + MockEfCore;

        // Exactly one diagnostic expected - VerifyAnalyzerAsync will fail if more are produced
        var expected = VerifyCS_LC010.Diagnostic("LC010")
            .WithSpan(18, 13, 18, 29)
            .WithArguments("SaveChanges");

        await VerifyCS_LC010.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Multiple distinct SaveChanges() calls in separate loops should each produce
    /// their own diagnostic - one per call site.
    /// </summary>
    [Fact]
    public async Task LC010_MultipleCallsInSeparateLoops_ProducesDistinctDiagnostics()
    {
        // Usings: lines 1-7
        // Line  8: (empty)
        // Line  9: class Program
        // Line 10: {
        // Line 11:     void Main()
        // Line 12:     {
        // Line 13:         var db = new MyDbContext();
        // Line 14:         var items = new List<int> { 1, 2, 3 };
        // Line 15:         (empty)
        // Line 16:         foreach (var item in items)
        // Line 17:         {
        // Line 18:             db.SaveChanges();
        // Line 19:         }
        // Line 20:         (empty)
        // Line 21:         for (int i = 0; i < 5; i++)
        // Line 22:         {
        // Line 23:             db.SaveChanges();
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            db.SaveChanges();
        }

        for (int i = 0; i < 5; i++)
        {
            db.SaveChanges();
        }
    }
}" + MockEfCore;

        var expected1 = VerifyCS_LC010.Diagnostic("LC010")
            .WithSpan(18, 13, 18, 29)
            .WithArguments("SaveChanges");

        var expected2 = VerifyCS_LC010.Diagnostic("LC010")
            .WithSpan(23, 13, 23, 29)
            .WithArguments("SaveChanges");

        await VerifyCS_LC010.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    /// <summary>
    /// A single sync call in an async method should produce exactly one LC008 diagnostic.
    /// </summary>
    [Fact]
    public async Task LC008_SingleSyncCall_ProducesExactlyOneDiagnostic()
    {
        // Usings: lines 1-7
        // Line  8: (empty)
        // Line  9: class Program
        // Line 10: {
        // Line 11:     async Task Main()
        // Line 12:     {
        // Line 13:         var db = new MyDbContext();
        // Line 14:         var users = db.Items.ToList();
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var users = db.Items.ToList();
        await Task.Delay(1);
    }
}" + MockEfCore;

        var expected = VerifyCS_LC008.Diagnostic("LC008")
            .WithSpan(14, 21, 14, 38)
            .WithArguments("ToList", "ToListAsync");

        await VerifyCS_LC008.VerifyAnalyzerAsync(test, expected);
    }

    #endregion
}
