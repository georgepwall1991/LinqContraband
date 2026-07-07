using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void RemoveRange(IEnumerable<TEntity> entities) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}
";

    // Same as EFCoreMock but also exposes the async ExecuteDeleteAsync extension that
    // ships alongside ExecuteDelete in EF Core 7+. Used to assert the fixer prefers the
    // async API (and an await) inside async contexts.
    private const string EFCoreMockWithAsync = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void RemoveRange(IEnumerable<TEntity> entities) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
        public static Task<int> ExecuteDeleteAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
";

    [Fact]
    public async Task Fixer_ShouldReplaceRemoveRangeWithExecuteDelete()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForMaterializedList()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var list = users.Where(x => x.Id > 0).ToList();
            users.RemoveRange(list);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForMixedRemoveRangeArguments()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
        public void RemoveRange(params object[] entities) { }
    }

    public class TestClass
    {
        public void TestMethod(AppDbContext db, User user)
        {
            var query = db.Users.Where(x => x.Id > 0);
            db.RemoveRange(query, user);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldUseExecuteDeleteAsyncAndAwait_InAsyncMethod()
    {
        // Inside an async method the synchronous ExecuteDelete() rewrite would inject a
        // blocking, sync-over-async database call (the exact smell LC008 flags). The fixer
        // must prefer the awaited ExecuteDeleteAsync() form instead.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMockWithAsync + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
            await Task.CompletedTask;
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMockWithAsync + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            await query.ExecuteDeleteAsync();
            await Task.CompletedTask;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_InAsyncContext_WhenExecuteDeleteAsyncUnavailable()
    {
        // The diagnostic still fires (synchronous ExecuteDelete exists), but the only
        // available rewrite in an async context would be the unsafe sync-over-async form,
        // so the fixer must decline rather than introduce a blocking call.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
            await Task.CompletedTask;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldUseSyncExecuteDelete_InSyncLocalFunctionWithinAsyncMethod()
    {
        // The nearest enclosing function is a synchronous local function, so await is
        // illegal here even though an async method is an ancestor. The fixer must emit the
        // synchronous ExecuteDelete() form rather than an await it cannot place.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMockWithAsync + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            void DeleteThem()
            {
                var query = users.Where(x => x.Id > 0);
                {|LC012:users.RemoveRange(query)|};
            }

            DeleteThem();
            await Task.CompletedTask;
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMockWithAsync + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            void DeleteThem()
            {
                var query = users.Where(x => x.Id > 0);
                // Warning: ExecuteDelete bypasses change tracking and cascades.
                query.ExecuteDelete();
            }

            DeleteThem();
            await Task.CompletedTask;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_RewritesAllRemoveRangeInvocations()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query1 = users.Where(x => x.Id > 0);
            {|#0:users.RemoveRange(query1)|};

            var query2 = users.Where(x => x.Id < 100);
            {|#1:users.RemoveRange(query2)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query1 = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query1.ExecuteDelete();

            var query2 = users.Where(x => x.Id < 100);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query2.ExecuteDelete();
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseExecuteDelete"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC012")
                .WithLocation(0)
                .WithArguments("RemoveRange"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC012")
                .WithLocation(1)
                .WithArguments("RemoveRange"));

        await testObj.RunAsync();
    }
}
