using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public partial class EntityMissingPrimaryKeyEdgeCasesTests
{
    private const string ManyToManyEntities = @"
    public class Post { public int Id { get; set; } }
    public class Tag { public int Id { get; set; } }
    public class PostTag
    {
        public int PostId { get; set; }
        public int TagId { get; set; }
    }
}";

    // Test scaffolding that hands out the referenced CollectionCollectionBuilder.
    private const string ManyToManyMock = ManyToManyEntities + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class ManyToManyBuilderExtensions
    {
        public static Microsoft.EntityFrameworkCore.Metadata.Builders.CollectionCollectionBuilder ManyToMany<T>(this EntityTypeBuilder<T> builder) where T : class
            => new Microsoft.EntityFrameworkCore.Metadata.Builders.CollectionCollectionBuilder();
    }
}";

    // LC011 only trusts a UsingEntity declared in a referenced EF Core assembly, so the builder is
    // compiled as its own project with EF Core's assembly name rather than declared in the test source.
    private const string CollectionCollectionBuilderSource = @"
using System;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class CollectionCollectionBuilder
    {
        public CollectionCollectionBuilder UsingEntity<TJoin>(Action<TJoin> configureJoin) where TJoin : class => this;
        public CollectionCollectionBuilder UsingEntity(string joinEntityName) => this;
    }
}";

    private static Task VerifyWithReferencedBuilderAsync(string source, string assemblyName = "Microsoft.EntityFrameworkCore")
    {
        var test = new AnalyzerTest { TestCode = source };
        test.TestState.AdditionalProjects[assemblyName].Sources.Add(("CollectionCollectionBuilder.cs", CollectionCollectionBuilderSource));
        test.TestState.AdditionalProjectReferences.Add(assemblyName);
        return test.RunAsync();
    }

    [Fact]
    public async Task TestInnocent_JoinEntityConfiguredWithUsingEntity_ShouldNotTrigger()
    {
        // Moonglade: HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<PostTagEntity>(...) with no HasKey.
        // EF Core keys the join entity with the composite of its two foreign keys.
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostTag> PostTags { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().ManyToMany().UsingEntity<PostTag>(j => { });
        }
    }
" + ManyToManyMock;

        await VerifyWithReferencedBuilderAsync(test);
    }

    [Fact]
    public async Task TestCrime_JoinEntityNotUsedAsUsingEntityType_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostTag> {|LC011:PostTags|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().ManyToMany().UsingEntity(""PostTag"");
        }
    }
" + ManyToManyMock;

        await VerifyWithReferencedBuilderAsync(test);
    }

    [Theory]
    [InlineData("Contoso.ModelBuilding")]
    [InlineData("Microsoft.EntityFrameworkCore.Extensions")]
    public async Task TestCrime_UsingEntityFromNonEfCoreAssemblyInEfCoreNamespace_ShouldTrigger(string assemblyName)
    {
        // The EF Core namespace is not enough: the declaring assembly must be EF Core's own, not one
        // whose name merely starts with it.
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostTag> {|LC011:PostTags|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().ManyToMany().UsingEntity<PostTag>(j => { });
        }
    }
" + ManyToManyMock;

        await VerifyWithReferencedBuilderAsync(test, assemblyName);
    }

    [Fact]
    public async Task TestCrime_UserDefinedUsingEntityExtension_ShouldTrigger()
    {
        // Only EF Core's own UsingEntity<TJoin> keys a join entity; a user method with the same name does not.
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostTag> {|LC011:PostTags|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().UsingEntity<PostTag>();
        }
    }

    public static class UserJoinExtensions
    {
        public static EntityTypeBuilder<Post> UsingEntity<TJoin>(this EntityTypeBuilder<Post> builder) where TJoin : class
            => builder;
    }
" + ManyToManyEntities;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SameProjectUsingEntityInEfCoreNamespace_ShouldTrigger()
    {
        // A project method declared inside the Microsoft.EntityFrameworkCore namespace is still the project's own.
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostTag> {|LC011:PostTags|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().UsingEntity<PostTag>();
        }
    }
" + ManyToManyEntities + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class ProjectJoinExtensions
    {
        public static EntityTypeBuilder<TestNamespace.Post> UsingEntity<TJoin>(this EntityTypeBuilder<TestNamespace.Post> builder) where TJoin : class
            => builder;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    [InlineData("GetType().Assembly")]
    [InlineData("this.GetType().Assembly")]
    public async Task TestInnocent_ApplyConfigurationsFromGetTypeAssembly_ShouldNotTrigger(string assemblyExpression)
    {
        // modular-monolith-with-ddd: modelBuilder.ApplyConfigurationsFromAssembly(this.GetType().Assembly).
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<GetTypeConfigEntity> GetTypeConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfigurationsFromAssembly(" + assemblyExpression + @");
    }

    public class GetTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class GetTypeConfigEntityConfiguration : IEntityTypeConfiguration<GetTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<GetTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ApplyConfigurationsFromOtherObjectsGetTypeAssembly_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<OtherGetTypeConfigEntity> {|LC011:OtherGetTypeConfigs|} { get; set; }
        private readonly string marker = """";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfigurationsFromAssembly(marker.GetType().Assembly);
    }

    public class OtherGetTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class OtherGetTypeConfigEntityConfiguration : IEntityTypeConfiguration<OtherGetTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<OtherGetTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AbstractContextApplyConfigurationsFromGetTypeAssembly_ShouldTrigger()
    {
        // GetType() on an abstract context returns a derived type that may live in another assembly,
        // so its configurations are not taken to be the ones in this assembly.
        var test = Usings + SemanticMockAttributes.Replace(
            "public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext",
            "public abstract class MyDbContext : Microsoft.EntityFrameworkCore.DbContext") + @"
        public DbSet<AbstractGetTypeConfigEntity> {|LC011:AbstractGetTypeConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }

    public class AbstractGetTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class AbstractGetTypeConfigEntityConfiguration : IEntityTypeConfiguration<AbstractGetTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<AbstractGetTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
