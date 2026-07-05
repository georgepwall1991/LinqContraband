using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public class FindInsteadOfFirstOrDefaultTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => null;
        public EntityTypeBuilder Entity(Type entityType) => null;
    }

    public class EntityTypeBuilder
    {
        public EntityTypeBuilder HasQueryFilter(LambdaExpression filter) => this;
    }

    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public void HasKey<TProperty>(Expression<Func<TEntity, TProperty>> keyExpression) { }
        public EntityTypeBuilder<TEntity> HasQueryFilter(Expression<Func<TEntity, bool>> filter) => this;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public TEntity Find(params object[] keyValues) => null;
        public ValueTask<TEntity> FindAsync(params object[] keyValues) => default;
        public ValueTask<TEntity> FindAsync(object[] keyValues, CancellationToken cancellationToken) => default;

        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task FirstOrDefault_WithId_ShouldTriggerLC023()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == 1)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithId_ShouldTriggerLC023()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await {|LC023:users.SingleOrDefaultAsync(x => x.Id == 1)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceFirstOrDefaultWithFind()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == 123)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = users.Find(123);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceSingleOrDefaultAsyncWithFindAsync()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await {|LC023:users.SingleOrDefaultAsync(x => x.Id == 456)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await users.FindAsync(456);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldPreserveCancellationTokenWhenReplacingAwaitedAsyncLookup()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users, int userId, CancellationToken cancellationToken)
        {
            var result = await {|LC023:users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users, int userId, CancellationToken cancellationToken)
        {
            var result = await users.FindAsync(new object[] { userId }, cancellationToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRewriteWhenKeyValueReferencesLambdaParameter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int OtherId { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == x.OtherId)|};
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRewriteAsyncLookupWhenCallIsNotAwaited()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public Task<User> TestMethod(DbSet<User> users, int userId, CancellationToken cancellationToken)
        {
            return {|LC023:users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)|};
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task First_WithId_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.First(x => x.Id == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithAsNoTracking_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNonId_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.FirstOrDefault(x => x.Name == ""abc"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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
    public async Task FirstOrDefault_WithId_WhenEntityHasQueryFilter_ShouldNotTrigger()
    {
        // Find checks the change tracker first, and a tracker hit bypasses global query
        // filters — so on a HasQueryFilter entity the rewrite can return an already-tracked
        // soft-deleted/other-tenant row the filtered query would not. Recommending Find
        // there is wrong advice, so the diagnostic is gated.
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
        // BaseEntity — but the filter is registered for User. The gate must check the DbSet's
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

    [Fact]
    public async Task FixAll_RewritesAllSyncLookups()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result1 = {|#0:users.FirstOrDefault(x => x.Id == 1)|};
            var result2 = {|#1:users.FirstOrDefault(x => x.Id == 2)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result1 = users.Find(1);
            var result2 = users.Find(2);
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseFind"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC023")
                .WithLocation(0)
                .WithArguments("FirstOrDefault"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC023")
                .WithLocation(1)
                .WithArguments("FirstOrDefault"));

        await testObj.RunAsync();
    }
}
