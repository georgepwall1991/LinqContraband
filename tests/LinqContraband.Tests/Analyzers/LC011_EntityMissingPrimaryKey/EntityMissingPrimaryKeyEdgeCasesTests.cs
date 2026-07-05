using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public class EntityMissingPrimaryKeyEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockAttributes = @"
namespace System.ComponentModel.DataAnnotations
{
    public class KeyAttribute : Attribute {}
}
namespace Microsoft.EntityFrameworkCore
{
    public class KeylessAttribute : Attribute {}
    public class PrimaryKeyAttribute : Attribute
    {
        public PrimaryKeyAttribute(params string[] propertyNames) {}
    }
    public class DbContext : IDisposable
    {
        public void Dispose() {}
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }
    public class DbSet<T> where T : class {}

    public interface IEntityTypeConfiguration<T> where T : class
    {
        void Configure(EntityTypeBuilder<T> builder);
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
    }

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(params string[] propertyNames) => this;
        public EntityTypeBuilder<T> HasKey(System.Linq.Expressions.Expression<Func<T, object>> keyExpression) => this;
        public OwnedNavigationBuilder<T, TOwned> OwnsOne<TOwned>(System.Linq.Expressions.Expression<Func<T, TOwned>> navigationExpression) where TOwned : class => new OwnedNavigationBuilder<T, TOwned>();
    }

    public class OwnedNavigationBuilder<T, TOwned> where T : class where TOwned : class {}
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
";

    private const string MockEnd = @"
    }
}";

    private const string SemanticMockAttributes = @"
namespace System.ComponentModel.DataAnnotations
{
    public class KeyAttribute : Attribute {}
}
namespace System.ComponentModel.DataAnnotations.Schema
{
    public class NotMappedAttribute : Attribute {}
}
namespace Microsoft.EntityFrameworkCore
{
    public class KeylessAttribute : Attribute {}
    public class PrimaryKeyAttribute : Attribute
    {
        public PrimaryKeyAttribute(params string[] propertyNames) {}
    }
    public class DbContext : IDisposable
    {
        public void Dispose() {}
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }
    public class DbSet<T> where T : class {}

    public interface IEntityTypeConfiguration<T> where T : class
    {
        void Configure(EntityTypeBuilder<T> builder);
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
        public void ApplyConfiguration<T>(IEntityTypeConfiguration<T> configuration) where T : class {}
        public void ApplyConfigurationsFromAssembly(System.Reflection.Assembly assembly) {}
    }

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(params string[] propertyNames) => this;
        public EntityTypeBuilder<T> HasKey(System.Linq.Expressions.Expression<Func<T, object>> keyExpression) => this;
        public EntityTypeBuilder<T> HasNoKey() => this;
        public EntityTypeBuilder<T> ToTable(string name) => this;
        public OwnedNavigationBuilder<T, TOwned> OwnsOne<TOwned>(System.Linq.Expressions.Expression<Func<T, TOwned>> navigationExpression) where TOwned : class => new OwnedNavigationBuilder<T, TOwned>();
        public OwnedNavigationBuilder<T, TOwned> OwnsMany<TOwned>(System.Linq.Expressions.Expression<Func<T, System.Collections.Generic.IEnumerable<TOwned>>> navigationExpression) where TOwned : class => new OwnedNavigationBuilder<T, TOwned>();
    }

    public class OwnedNavigationBuilder<T, TOwned> where T : class where TOwned : class {}
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
";

    [Fact]
    public async Task TestInnocent_EntityInheritsIdFromBaseClass_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<DerivedEntity> DerivedEntities { get; set; }
    }

    public abstract class EntityBase
    {
        public int Id { get; set; }
    }

    public class DerivedEntity : EntityBase
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityInheritsIdFromDeepBaseClass_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<GrandChildEntity> GrandChildren { get; set; }
    }

    public abstract class RootBase
    {
        public int Id { get; set; }
    }

    public abstract class MiddleBase : RootBase
    {
        public string CreatedBy { get; set; }
    }

    public class GrandChildEntity : MiddleBase
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityInheritsTypeNameIdFromBaseClass_ShouldNotTrigger()
    {
        // Base class has EntityNameId pattern - should be recognized via convention
        var test = Usings + MockAttributes + @"
        public DbSet<Product> Products { get; set; }
    }

    public abstract class BaseEntity
    {
        public Guid ProductId { get; set; }
    }

    public class Product : BaseEntity
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CompositeKeyViaKeyAttributes_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<OrderItem> OrderItems { get; set; }
    }

    public class OrderItem
    {
        [Key]
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_CompositeKeyViaFluentApi_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderItem>().HasKey(""OrderId"", ""ProductId"");
        }
    }

    public class OrderItem
    {
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_OwnedTypeViaOwnsOne_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().OwnsOne<ShippingAddress>(o => o.ShippingAddress);
        }
    }

    public class Order
    {
        public int Id { get; set; }
        public ShippingAddress ShippingAddress { get; set; }
    }

    // Owned type - no primary key needed
    public class ShippingAddress
    {
        public string Street { get; set; }
        public string City { get; set; }
        public string ZipCode { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_EntityWithNonPublicId_ShouldTrigger()
    {
        // A private Id property does not count as a valid key
        var test = Usings + MockAttributes + @"
        public DbSet<SecretEntity> Secrets { get; set; }
    }

    public class SecretEntity
    {
        private int Id { get; set; }
        internal string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(51, 36, 51, 43)
            .WithArguments("SecretEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_EntityWithOnlyNavigationProperties_ShouldTrigger()
    {
        // Entity with no valid key type properties should trigger
        var test = Usings + MockAttributes + @"
        public DbSet<BadEntity> BadEntities { get; set; }
    }

    public class OtherThing
    {
        public int Id { get; set; }
    }

    public class BadEntity
    {
        public OtherThing Parent { get; set; }
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(51, 33, 51, 44)
            .WithArguments("BadEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_EntityWithKeyAttributeOnNonConventionalName_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<CustomKeyEntity> CustomKeys { get; set; }
    }

    public class CustomKeyEntity
    {
        [Key]
        public int MySpecialIdentifier { get; set; }
        public string Data { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithPrimaryKeyAttributeComposite_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<CompositePkEntity> CompositePks { get; set; }
    }

    [PrimaryKey(nameof(TenantId), nameof(Code))]
    public class CompositePkEntity
    {
        public int TenantId { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeylessEntityWithNoProperties_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ViewResult> ViewResults { get; set; }
    }

    [Keyless]
    public class ViewResult
    {
        public string Value { get; set; }
        public int Count { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_FakeAttributeNames_ShouldNotSuppressDiagnostic()
    {
        var test = @"
using System;
using FakeAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FakeAnnotations
{
    public class KeyAttribute : Attribute {}
    public class KeylessAttribute : Attribute {}
    public class PrimaryKeyAttribute : Attribute
    {
        public PrimaryKeyAttribute(params string[] propertyNames) {}
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() {}
    }

    public class DbSet<T> where T : class {}
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public DbSet<FakeKeyEntity> {|LC011:FakeKeys|} { get; set; }
        public DbSet<FakeKeylessEntity> {|LC011:FakeKeyless|} { get; set; }
        public DbSet<FakePrimaryKeyEntity> {|LC011:FakePrimaryKeys|} { get; set; }
    }

    public class FakeKeyEntity
    {
        [Key]
        public int Code { get; set; }
    }

    [Keyless]
    public class FakeKeylessEntity
    {
        public string Name { get; set; }
    }

    [PrimaryKey(nameof(Code))]
    public class FakePrimaryKeyEntity
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_NotMappedId_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<IgnoredIdEntity> {|LC011:IgnoredIds|} { get; set; }
    }

    public class IgnoredIdEntity
    {
        [NotMapped]
        public int Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_UnappliedEntityTypeConfiguration_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ConfiguredElsewhereEntity> {|LC011:ConfiguredElsewhere|} { get; set; }
    }

    public class ConfiguredElsewhereEntity
    {
        public int Code { get; set; }
    }

    public class ConfiguredElsewhereEntityConfiguration : IEntityTypeConfiguration<ConfiguredElsewhereEntity>
    {
        public void Configure(EntityTypeBuilder<ConfiguredElsewhereEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AppliedEntityTypeConfiguration_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<AppliedConfigEntity> AppliedConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AppliedConfigEntityConfiguration());
        }
    }

    public class AppliedConfigEntity
    {
        public int Code { get; set; }
    }

    public class AppliedConfigEntityConfiguration : IEntityTypeConfiguration<AppliedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<AppliedConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_BuilderVariableHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<VariableConfiguredEntity> VariableConfiguredEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<VariableConfiguredEntity>();
            entity.HasKey(e => e.Code);
        }
    }

    public class VariableConfiguredEntity
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ChainedBuilderVariableHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ChainedConfiguredEntity> ChainedConfiguredEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<ChainedConfiguredEntity>();
            entity.ToTable(""ChainedConfiguredEntities"").HasKey(e => e.Code);
        }
    }

    public class ChainedConfiguredEntity
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfReferentialBuilderLocal_ShouldNotCrashAnalyzer()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<SelfReferentialBuilderEntity> {|LC011:SelfReferentialBuilderEntities|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = {|CS0841:entity|}.HasKey(""Id"");
        }
    }

    public class SelfReferentialBuilderEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AppliedConfigurationChainedBuilderHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ChainedConfigEntity> ChainedConfigEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ChainedConfigEntityConfiguration());
        }
    }

    public class ChainedConfigEntity
    {
        public int Code { get; set; }
    }

    public class ChainedConfigEntityConfiguration : IEntityTypeConfiguration<ChainedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ChainedConfigEntity> builder)
        {
            builder.ToTable(""ChainedConfigEntities"").HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalVariableAppliedConfiguration_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<VariableAppliedConfigEntity> VariableAppliedConfigEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var config = new VariableAppliedConfigEntityConfiguration();
            modelBuilder.ApplyConfiguration(config);
        }
    }

    public class VariableAppliedConfigEntity
    {
        public int Code { get; set; }
    }

    public class VariableAppliedConfigEntityConfiguration : IEntityTypeConfiguration<VariableAppliedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<VariableAppliedConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ExternalApplyConfigurationsFromAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ExternalAssemblyConfigEntity> {|LC011:ExternalAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(string).Assembly);
        }
    }

    public class ExternalAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExternalAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExternalAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExternalAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ApplyConfigurationsFromExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ExecutingAssemblyConfigEntity> ExecutingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    public class ExecutingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExecutingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExecutingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExecutingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ApplyConfigurationsFromImportedExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ImportedAssemblyConfigEntity> ImportedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ImportedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ImportedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ImportedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ImportedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AliasedSystemReflectionAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using Assembly = System.Reflection.Assembly;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<SystemReflectionAliasAssemblyConfigEntity> SystemReflectionAliasAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class SystemReflectionAliasAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class SystemReflectionAliasAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<SystemReflectionAliasAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<SystemReflectionAliasAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_GlobalQualifiedExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<GlobalQualifiedAssemblyConfigEntity> GlobalQualifiedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(global::System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    public class GlobalQualifiedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class GlobalQualifiedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<GlobalQualifiedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<GlobalQualifiedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ShadowedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ShadowedAssemblyTypeConfigEntity> {|LC011:ShadowedAssemblyTypeConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class ShadowedAssemblyTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedAssemblyTypeConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedAssemblyTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedAssemblyTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<LocalAssemblyValueConfigEntity> {|LC011:LocalAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var Assembly = new ExternalAssemblyProvider();
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class LocalAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class LocalAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<LocalAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<LocalAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MemberAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        private readonly ExternalAssemblyProvider Assembly = new ExternalAssemblyProvider();

        public DbSet<MemberAssemblyValueConfigEntity> {|LC011:MemberAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class MemberAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class MemberAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<MemberAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MemberAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ForeachAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ForeachAssemblyValueConfigEntity> {|LC011:ForeachAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var Assembly in new[] { new ExternalAssemblyProvider() })
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class ForeachAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class ForeachAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<ForeachAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ForeachAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_CatchAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<CatchAssemblyValueConfigEntity> {|LC011:CatchAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            try
            {
            }
            catch (ExternalAssemblyException Assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyException : Exception
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class CatchAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class CatchAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<CatchAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<CatchAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_PatternAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<PatternAssemblyValueConfigEntity> {|LC011:PatternAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            object provider = new ExternalAssemblyProvider();
            if (provider is ExternalAssemblyProvider Assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class PatternAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class PatternAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<PatternAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<PatternAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ParentNamespaceAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace.Inner;") + SemanticMockAttributes.Replace(
            "namespace TestNamespace\n{\n    public class MyDbContext",
            @"namespace TestNamespace
{
    public class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    namespace Inner
    {
        public class MyDbContext") + @"
        public DbSet<ParentNamespaceAssemblyConfigEntity> {|LC011:ParentNamespaceAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ParentNamespaceAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ParentNamespaceAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ParentNamespaceAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ParentNamespaceAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AliasedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing Assembly = ExternalAssemblyProvider.Assembly;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<AliasedAssemblyTypeConfigEntity> {|LC011:AliasedAssemblyTypeConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class AliasedAssemblyTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class AliasedAssemblyTypeConfigEntityConfiguration : IEntityTypeConfiguration<AliasedAssemblyTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<AliasedAssemblyTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}

namespace ExternalAssemblyProvider
{
    public static class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_GlobalUsingExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Usings + SemanticMockAttributes + @"
        public DbSet<GlobalUsingAssemblyConfigEntity> GlobalUsingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class GlobalUsingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class GlobalUsingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<GlobalUsingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<GlobalUsingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}"
        };
        test.TestState.Sources.Add(("GlobalUsings.cs", "global using System.Reflection;"));

        await test.RunAsync();
    }

    [Fact]
    public async Task TestInnocent_NamespaceUsingExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "namespace TestNamespace\n{\n    public class MyDbContext",
            "namespace TestNamespace\n{\n    using System.Reflection;\n\n    public class MyDbContext") + @"
        public DbSet<NamespaceUsingAssemblyConfigEntity> NamespaceUsingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class NamespaceUsingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class NamespaceUsingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<NamespaceUsingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<NamespaceUsingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<LocalAssemblyConfigEntity> LocalAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class LocalAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class LocalAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<LocalAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<LocalAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ReassignedLocalAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ReassignedAssemblyConfigEntity> {|LC011:ReassignedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            assembly = typeof(string).Assembly;
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class ReassignedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ReassignedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ReassignedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ReassignedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionallyReassignedLocalAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ConditionallyReassignedAssemblyConfigEntity> {|LC011:ConditionallyReassignedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            if (Environment.TickCount > 0)
            {
                assembly = typeof(string).Assembly;
            }

            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class ConditionallyReassignedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ConditionallyReassignedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ConditionallyReassignedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ConditionallyReassignedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UninvokedLocalFunctionAssignment_ShouldNotInvalidateCurrentAssembly()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<UninvokedLocalFunctionAssemblyConfigEntity> UninvokedLocalFunctionAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            void Later()
            {
                assembly = typeof(string).Assembly;
            }

            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class UninvokedLocalFunctionAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class UninvokedLocalFunctionAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<UninvokedLocalFunctionAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<UninvokedLocalFunctionAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalAssemblyShadowsCurrentAssemblyMember_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<ShadowedAssemblyConfigEntity> {|LC011:ShadowedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var ConfigAssembly = typeof(string).Assembly;
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ShadowedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MutableMemberAssemblyField_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MutableFieldAssemblyConfigEntity> {|LC011:MutableFieldAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MutableFieldAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MutableFieldAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MutableFieldAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MutableFieldAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MutableMemberAssemblyProperty_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static System.Reflection.Assembly ConfigAssembly { get; set; } = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MutablePropertyAssemblyConfigEntity> {|LC011:MutablePropertyAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MutablePropertyAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MutablePropertyAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MutablePropertyAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MutablePropertyAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InheritedReadonlyAssemblyMember_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext",
            @"public class BaseDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        protected static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();
    }

    public class MyDbContext : BaseDbContext") + @"
        public DbSet<InheritedMemberAssemblyConfigEntity> InheritedMemberAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class InheritedMemberAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class InheritedMemberAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<InheritedMemberAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<InheritedMemberAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DerivedMutableAssemblyMemberShadowsInheritedReadonlyMember_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext",
            @"public class BaseDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        protected static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();
    }

    public class MyDbContext : BaseDbContext") + @"
        private static new System.Reflection.Assembly ConfigAssembly = typeof(string).Assembly;

        public DbSet<ShadowedInheritedAssemblyConfigEntity> {|LC011:ShadowedInheritedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ShadowedInheritedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedInheritedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedInheritedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedInheritedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ExpressionBodiedMemberAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        private static Assembly ConfigAssembly => Assembly.GetExecutingAssembly();

        public DbSet<ExpressionBodiedAssemblyConfigEntity> ExpressionBodiedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ExpressionBodiedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExpressionBodiedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExpressionBodiedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExpressionBodiedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfReferentialAssemblyLocal_ShouldNotCrashAnalyzer()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<SelfReferentialAssemblyConfigEntity> {|LC011:SelfReferentialAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = {|CS0841:assembly|};
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class SelfReferentialAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class SelfReferentialAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<SelfReferentialAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<SelfReferentialAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_MemberExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MemberAssemblyConfigEntity> MemberAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MemberAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MemberAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MemberAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MemberAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ScopedBuilderVariableReuse_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ScopedOrder> ScopedOrders { get; set; }
        public DbSet<ScopedCustomer> ScopedCustomers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            {
                var entity = modelBuilder.Entity<ScopedOrder>();
                entity.HasKey(e => e.Code);
            }

            {
                var entity = modelBuilder.Entity<ScopedCustomer>();
                entity.HasKey(e => e.Code);
            }
        }
    }

    public class ScopedOrder
    {
        public int Code { get; set; }
    }

    public class ScopedCustomer
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NonGenericOwnsOne_ShouldNotTriggerForOwnedDbSetType()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<Order> Orders { get; set; }
        public DbSet<ShippingAddress> ShippingAddresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress);
        }
    }

    public class Order
    {
        public int Id { get; set; }
        public ShippingAddress ShippingAddress { get; set; }
    }

    public class ShippingAddress
    {
        public string Street { get; set; }
        public string City { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
