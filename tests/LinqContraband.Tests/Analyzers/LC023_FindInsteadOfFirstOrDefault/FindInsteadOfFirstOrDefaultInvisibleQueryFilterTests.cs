using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Query filters LC023 cannot read: configured by a base DbContext in another assembly
/// (fullstackhero's BaseDbContext, Finbuckle's MultiTenantDbContext), by non-generic
/// Entity(Type)/SetQueryFilter loops, or by generic helpers. Find bypasses query filters for
/// tracked entities, so the rule must stay quiet whenever an entity may be filtered.
/// </summary>
public partial class FindInsteadOfFirstOrDefaultTests
{
    private const string InvisibleFilterExtraMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public interface IDbContextFactory<TContext> where TContext : DbContext
    {
        TContext CreateDbContext();
        Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata
{
    public interface IMutableEntityType
    {
        Type ClrType { get; }
        void SetQueryFilter(LambdaExpression filter);
    }
}
";

    private const string InvisibleFilterEntities = @"
namespace Shop.Domain
{
    public interface ISoftDeletable { bool IsDeleted { get; set; } }
    public class User : ISoftDeletable { public int Id { get; set; } public bool IsDeleted { get; set; } }
    public class Product { public int Id { get; set; } }
}
";

    // FSH-style base context: OnModelCreating loops over entity types and calls the
    // non-generic Entity(Type).HasQueryFilter, all in a referenced assembly.
    private const string LoopingBaseContextLibrary = @"
namespace Shared.Persistence
{
    using Microsoft.EntityFrameworkCore;
    using Shop.Domain;

    public abstract class BaseDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var type in new[] { typeof(User) })
            {
                Expression<Func<ISoftDeletable, bool>> filter = x => !x.IsDeleted;
                modelBuilder.Entity(type).HasQueryFilter(filter);
            }
        }
    }
}
";

    private const string PlainBaseContextLibrary = @"
namespace Shared.Persistence
{
    using Microsoft.EntityFrameworkCore;

    public abstract class BaseDbContext : DbContext
    {
        public int AuditLevel { get; set; }
    }
}
";

    private const string IdentityBaseContextLibrary = @"
namespace Microsoft.AspNetCore.Identity.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore;

    public class IdentityDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { }
    }
}
";

    private const string FinbuckleBaseContextLibrary = @"
namespace Finbuckle.MultiTenant.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore;

    public abstract class MultiTenantDbContext : DbContext
    {
    }
}
";

    private static async Task<MetadataReference> BuildInvisibleFilterLibraryAsync(string librarySource)
    {
        var library = CSharpCompilation.Create(
            "Shared.Persistence",
            new[]
            {
                CSharpSyntaxTree.ParseText(EFCoreMock + InvisibleFilterExtraMock + InvisibleFilterEntities + librarySource)
            },
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, default),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        var emit = library.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static async Task VerifyAgainstLibraryAsync(string librarySource, string testCode)
    {
        var test = new AnalyzerTest
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(await BuildInvisibleFilterLibraryAsync(librarySource));
        await test.RunAsync();
    }

    private const string DerivedContextQuery = @"
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shop.Domain;

namespace Shop.App
{
    public class AppDbContext : Shared.Persistence.BaseDbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Product> Products { get; set; }
    }

    public class Queries
    {
        public User ById(AppDbContext db, int id) => [|db.Users.FirstOrDefault(x => x.Id == id)|];
        public Product ProductById(AppDbContext db, int id) => [|db.Products.FirstOrDefault(x => x.Id == id)|];
    }
}";

    private static string DerivedContextQueryExpecting(bool reports) =>
        reports
            ? DerivedContextQuery.Replace("[|", "{|LC023:").Replace("|]", "|}")
            : DerivedContextQuery.Replace("[|", string.Empty).Replace("|]", string.Empty);

    [Fact]
    public async Task FirstOrDefault_OnContextWhoseMetadataBaseOverridesOnModelCreating_ShouldNotTrigger()
    {
        // The base context's OnModelCreating lives in a referenced assembly: its filters are
        // invisible, so every entity of the derived context may be filtered.
        await VerifyAgainstLibraryAsync(LoopingBaseContextLibrary, DerivedContextQueryExpecting(reports: false));
    }

    [Fact]
    public async Task FirstOrDefault_OnContextWhoseMetadataBaseDoesNotOverrideOnModelCreating_ShouldTrigger()
    {
        // A referenced base context without an OnModelCreating override configures nothing.
        await VerifyAgainstLibraryAsync(PlainBaseContextLibrary, DerivedContextQueryExpecting(reports: true));
    }

    [Fact]
    public async Task FirstOrDefault_OnContextDerivingFromMicrosoftIdentityBase_ShouldTrigger()
    {
        // IdentityDbContext overrides OnModelCreating but adds no query filters; counting it
        // would silence the rule in every ASP.NET Core Identity app.
        var testCode = DerivedContextQueryExpecting(reports: true)
            .Replace("Shared.Persistence.BaseDbContext", "Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext");
        await VerifyAgainstLibraryAsync(IdentityBaseContextLibrary, testCode);
    }

    [Fact]
    public async Task FirstOrDefault_OnFinbuckleMultiTenantContext_ShouldNotTrigger()
    {
        // Finbuckle's MultiTenantDbContext adds a tenant filter to every [MultiTenant] entity.
        var testCode = DerivedContextQueryExpecting(reports: false)
            .Replace("Shared.Persistence.BaseDbContext", "Finbuckle.MultiTenant.EntityFrameworkCore.MultiTenantDbContext");
        await VerifyAgainstLibraryAsync(FinbuckleBaseContextLibrary, testCode);
    }

    [Fact]
    public async Task FirstOrDefault_OnContextDeclaredInReferencedAssembly_ShouldNotTrigger()
    {
        // The context itself (and its OnModelCreating) lives in the referenced assembly.
        const string library = @"
namespace Shared.Persistence
{
    using Microsoft.EntityFrameworkCore;
    using Shop.Domain;

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
";
        const string testCode = @"
using System.Linq;
using Shared.Persistence;
using Shop.Domain;

namespace Shop.App
{
    public class Queries
    {
        public User ById(AppDbContext db, int id) => db.Users.FirstOrDefault(x => x.Id == id);
    }
}";
        await VerifyAgainstLibraryAsync(library, testCode);
    }

    [Fact]
    public async Task FirstOrDefault_OnDbSetParameter_WhenSourceContextHasInvisibleFilters_ShouldNotTrigger()
    {
        // The DbSet's context is unknown here; a context in this project whose model
        // configuration is invisible could be the one that owns it.
        const string testCode = @"
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shop.Domain;

namespace Shop.App
{
    public class AppDbContext : Shared.Persistence.BaseDbContext
    {
    }

    public class Queries
    {
        public User ById(DbSet<User> users, int id) => users.FirstOrDefault(x => x.Id == id);
    }
}";
        await VerifyAgainstLibraryAsync(LoopingBaseContextLibrary, testCode);
    }

    [Fact]
    public async Task FirstOrDefault_OnSourceContextProperty_WithoutFilters_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(x => x.Id);
        }
    }

    public class TestClass
    {
        public User TestMethod(AppDbContext db, int id)
        {
            return {|LC023:db.Users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WhenSourceLoopsNonGenericEntityHasQueryFilter_ShouldNotTrigger()
    {
        // modelBuilder.Entity(type).HasQueryFilter(...) with a runtime Type: the filtered
        // entities cannot be named, so any entity may be filtered.
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
            foreach (var type in new[] { typeof(User) })
            {
                Expression<Func<User, bool>> filter = x => !x.IsDeleted;
                modelBuilder.Entity(type).HasQueryFilter(filter);
            }
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
    public async Task FirstOrDefault_WhenSourceLoopsMutableEntityTypeSetQueryFilter_ShouldNotTrigger()
    {
        // foreach (var et in modelBuilder.Model.GetEntityTypes()) et.SetQueryFilter(...)
        var test = @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + InvisibleFilterExtraMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }

    public static class SoftDeleteSetup
    {
        public static void Apply(IMutableEntityType[] entityTypes, LambdaExpression filter)
        {
            foreach (var entityType in entityTypes)
                entityType.SetQueryFilter(filter);
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
    public async Task FirstOrDefault_WhenSetQueryFilterIsNotOnEntityTypeMetadata_ShouldTrigger()
    {
        // A SetQueryFilter lookalike under an EF Core namespace that is not entity-type metadata does not
        // configure a query filter, so it must not silence the rule for every entity.
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;" + EFCoreMock + InvisibleFilterExtraMock + @"
namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public class FilterLog { public void SetQueryFilter(string name) { } }
}

namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, Microsoft.EntityFrameworkCore.Diagnostics.FilterLog log, int id)
        {
            log.SetQueryFilter(""soft-delete"");
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WhenGenericHelperFiltersConstrainedEntities_ShouldOnlyTriggerOnUnconstrainedEntity()
    {
        // ApplySoftDelete<T>() where T : ISoftDeletable filters every entity that satisfies
        // the constraint; an entity that does not satisfy it is still unfiltered.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public interface ISoftDeletable { bool IsDeleted { get; set; } }
    public class User : ISoftDeletable { public int Id { get; set; } public bool IsDeleted { get; set; } }
    public class Product { public int Id { get; set; } }

    public static class SoftDeleteSetup
    {
        public static void ApplySoftDelete<T>(ModelBuilder modelBuilder) where T : class, ISoftDeletable
        {
            modelBuilder.Entity<T>().HasQueryFilter(x => !x.IsDeleted);
        }
    }

    public class TestClass
    {
        public User UserById(DbSet<User> users, int id) => users.FirstOrDefault(x => x.Id == id);
        public Product ProductById(DbSet<Product> products, int id) => {|LC023:products.FirstOrDefault(x => x.Id == id)|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WhenEntityIsMultiTenantConfigured_ShouldNotTrigger()
    {
        // Finbuckle's IsMultiTenant() adds a tenant query filter to the entity.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace Finbuckle.MultiTenant
{
    public static class EntityTypeBuilderExtensions
    {
        public static Microsoft.EntityFrameworkCore.EntityTypeBuilder<T> IsMultiTenant<T>(this Microsoft.EntityFrameworkCore.EntityTypeBuilder<T> builder) where T : class => builder;
    }
}

namespace LinqContraband.Test
{
    using Finbuckle.MultiTenant;

    public class User { public int Id { get; set; } }
    public class Product { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().IsMultiTenant();
        }
    }

    public class TestClass
    {
        public User UserById(DbSet<User> users, int id) => users.FirstOrDefault(x => x.Id == id);
        public Product ProductById(DbSet<Product> products, int id) => {|LC023:products.FirstOrDefault(x => x.Id == id)|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WhenEntityHasFinbuckleMultiTenantAttribute_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace Finbuckle.MultiTenant
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class MultiTenantAttribute : System.Attribute { }
}

namespace LinqContraband.Test
{
    [Finbuckle.MultiTenant.MultiTenant]
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User UserById(DbSet<User> users, int id) => users.FirstOrDefault(x => x.Id == id);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_OnContextFromDbContextFactory_ShouldNotTrigger()
    {
        // A context fresh from IDbContextFactory has an empty change tracker: Find would run
        // the same query, so the advice is noise.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + InvisibleFilterExtraMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class AppDbContext : DbContext, IDisposable, IAsyncDisposable
    {
        public DbSet<User> Users { get; set; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }

    public class TestClass
    {
        public User ById(IDbContextFactory<AppDbContext> factory, int id)
        {
            using var db = factory.CreateDbContext();
            return db.Users.FirstOrDefault(x => x.Id == id);
        }

        public async Task<User> ByIdAsync(IDbContextFactory<AppDbContext> factory, int id)
        {
            await using var db = await factory.CreateDbContextAsync();
            return await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        }
    }
}";

        await VerifyAgainstSourceAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_OnInjectedContext_NextToFactoryUsage_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + InvisibleFilterExtraMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public class TestClass
    {
        private readonly AppDbContext db;

        public TestClass(AppDbContext db) { this.db = db; }

        public User ById(int id)
        {
            return {|LC023:db.Users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}";

        await VerifyAgainstSourceAsync(test);
    }

    private static Task VerifyAgainstSourceAsync(string testCode)
    {
        // await using needs IAsyncDisposable and ValueTask from a modern runtime.
        var test = new AnalyzerTest
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        return test.RunAsync();
    }
}
