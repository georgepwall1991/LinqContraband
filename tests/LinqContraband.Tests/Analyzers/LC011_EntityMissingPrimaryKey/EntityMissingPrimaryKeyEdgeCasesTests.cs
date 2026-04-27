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
