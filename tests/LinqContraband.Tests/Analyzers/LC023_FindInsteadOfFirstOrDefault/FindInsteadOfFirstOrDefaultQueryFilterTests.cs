using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public partial class FindInsteadOfFirstOrDefaultTests
{
    [Fact]
    public async Task FirstOrDefault_WithId_WhenEntityHasQueryFilter_ShouldNotTrigger()
    {
        // Find checks the change tracker first, and a tracker hit bypasses global query
        // filters. On a HasQueryFilter entity, the rewrite can return an already-tracked
        // soft-deleted/other-tenant row the filtered query would not, so stay quiet.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithId_WhenEntityHasQueryFilter_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public async Task<User> TestMethod(DbSet<User> users, int id)
        {
            return await users.FirstOrDefaultAsync(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithId_WhenDifferentEntityHasQueryFilter_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class Tenant { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tenant>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithId_WhenQueryFilterConfiguredInSeparateClass_ShouldNotTrigger()
    {
        // IEntityTypeConfiguration-style setup: the filter lives on an EntityTypeBuilder<T>
        // parameter in another class, found by the cross-tree scan.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class UserConfiguration
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithInheritedKey_WhenEntityHasQueryFilter_ShouldNotTrigger()
    {
        // The Id is declared on a base class, so the predicate property's containing type is
        // BaseEntity, but the filter is registered for User. The gate must check the DbSet's
        // entity type, not the key's declaring type.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class BaseEntity { public int Id { get; set; } }
    public class User : BaseEntity { public bool IsDeleted { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WhenBaseEntityHasQueryFilter_ShouldNotTrigger()
    {
        // EF only allows filters on the hierarchy root and propagates them to derived
        // entities, so a filter on the base type must also gate lookups on the derived set.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class BaseEntity { public int Id { get; set; } public bool IsDeleted { get; set; } }
    public class User : BaseEntity { }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BaseEntity>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNonGenericBuilderQueryFilter_ShouldNotTrigger()
    {
        // Shared soft-delete setup often uses the non-generic builder:
        // modelBuilder.Entity(typeof(User)).HasQueryFilter(...). The entity type comes from
        // the Entity(Type) call's typeof argument.
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Expression<Func<User, bool>> filter = x => !x.IsDeleted;
            modelBuilder.Entity(typeof(User)).HasQueryFilter(filter);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithUnrelatedGenericHasQueryFilter_ShouldStillTrigger()
    {
        // A lookalike HasQueryFilter on a non-EF builder type must not suppress the rule.
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public class FilterMetadata<TEntity>
    {
        public void HasQueryFilter(Expression<Func<TEntity, bool>> filter) { }
    }

    public class Setup
    {
        public void Configure()
        {
            new FilterMetadata<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
