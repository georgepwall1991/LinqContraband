using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public class OptimizeRemoveRangeFixerTests
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
    public async Task Fixer_ShouldNotRegister_WhenRemoveRangeIsFollowedBySaveChanges()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public class TestClass
    {
        public void TestMethod(AppDbContext db)
        {
            var query = db.Users.Where(x => x.Id > 0);
            db.Users.RemoveRange(query);
            db.SaveChanges();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenOuterBlockSaveChangesFollowsRemoveRange()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public class TestClass
    {
        public void TestMethod(AppDbContext db, bool shouldDelete)
        {
            var query = db.Users.Where(x => x.Id > 0);
            if (shouldDelete)
            {
                db.Users.RemoveRange(query);
            }

            db.SaveChanges();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
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
}
