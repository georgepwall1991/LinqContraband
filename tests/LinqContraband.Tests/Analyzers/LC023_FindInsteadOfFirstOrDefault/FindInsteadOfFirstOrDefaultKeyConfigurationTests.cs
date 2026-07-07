using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public partial class FindInsteadOfFirstOrDefaultTests
{
    [Fact]
    public async Task FirstOrDefault_WithConventionId_WhenFluentApiConfiguresDifferentKey_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(x => x.ExternalId);
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
    public async Task FirstOrDefault_WithFluentApiConfiguredKey_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(x => x.ExternalId);
        }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int externalId)
        {
            return {|LC023:users.FirstOrDefault(x => x.ExternalId == externalId)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithUnrelatedGenericHasKey_ShouldIgnoreHelperAndTriggerConventionKey()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class KeyMetadata<TEntity>
    {
        public void HasKey<TProperty>(Expression<Func<TEntity, TProperty>> keyExpression) { }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            new KeyMetadata<User>().HasKey(x => x.ExternalId);
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithUnrelatedGenericHasKey_ShouldNotTriggerForHelperKey()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class KeyMetadata<TEntity>
    {
        public void HasKey<TProperty>(Expression<Func<TEntity, TProperty>> keyExpression) { }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int externalId)
        {
            new KeyMetadata<User>().HasKey(x => x.ExternalId);
            return users.FirstOrDefault(x => x.ExternalId == externalId);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithFakeKeyAttribute_ShouldNotTriggerForFakeKey()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace CustomAnnotations
{
    public sealed class KeyAttribute : System.Attribute { }
}

namespace LinqContraband.Test
{
    public class User
    {
        [CustomAnnotations.Key]
        public int ExternalId { get; set; }
        public int Id { get; set; }
    }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int externalId)
        {
            return users.FirstOrDefault(x => x.ExternalId == externalId);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithPartialCompositeFluentApiKey_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int TenantId { get; set; } public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(x => new { x.TenantId, x.Id });
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
}
