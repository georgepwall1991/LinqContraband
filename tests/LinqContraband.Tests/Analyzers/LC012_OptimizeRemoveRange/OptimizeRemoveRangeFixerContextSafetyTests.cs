using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
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
    public async Task Fixer_ShouldRegister_WhenSaveChangesIsInMutuallyExclusiveElseBranch()
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
                {|LC012:db.Users.RemoveRange(query)|};
            }
            else
            {
                db.SaveChanges();
            }
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
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
                // Warning: ExecuteDelete bypasses change tracking and cascades.
                query.ExecuteDelete();
            }
            else
            {
                db.SaveChanges();
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenSaveChangesUsesDifferentFreshContext()
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
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = deleteDb.Users.Where(x => x.Id > 0);

            {|LC012:deleteDb.Users.RemoveRange(query)|};
            saveDb.SaveChanges();
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
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
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = deleteDb.Users.Where(x => x.Id > 0);

            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
            saveDb.SaveChanges();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenQuerySourceBelongsToLaterSaveContext()
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
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = saveDb.Users.Where(x => x.Id > 0);

            {|LC012:deleteDb.Users.RemoveRange(query)|};
            saveDb.SaveChanges();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenQuerySourceComesFromAmbiguousHelper()
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
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = Pick(deleteDb.Users, saveDb.Users);

            {|LC012:deleteDb.Users.RemoveRange(query)|};
            saveDb.SaveChanges();
        }

        private static IQueryable<User> Pick(IQueryable<User> first, IQueryable<User> second) => second;
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenQueryCombinesLaterSaveContextSource()
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
        public void TestMethod()
        {
            var deleteDb = new AppDbContext();
            var saveDb = new AppDbContext();
            var query = deleteDb.Users.Concat(saveDb.Users);

            {|LC012:deleteDb.Users.RemoveRange(query)|};
            saveDb.SaveChanges();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
