using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public class EntityMissingPrimaryKeyTests
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
    }
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
";

    private const string MockEnd = @"
    }
}";

    [Fact]
    public async Task TestCrime_EntityNoKey_ShouldTriggerLC011()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NoKeyEntity> NoKeys { get; set; }
    }

    public class NoKeyEntity
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 35, 48, 41) // Adjust based on exact line count
            .WithArguments("NoKeyEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_EntityWithId_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ValidEntity> ValidEntities { get; set; }
    }

    public class ValidEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithTypeNameId_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<User> Users { get; set; }
    }

    public class User
    {
        public int UserId { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithKeyAttribute_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<KeyEntity> KeyEntities { get; set; }
    }

    public class KeyEntity
    {
        [Key]
        public int CustomKey { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithPrimaryKeyAttribute_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<PkEntity> PkEntities { get; set; }
    }

    [PrimaryKey(nameof(Name))]
    public class PkEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeylessEntity_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<KeylessView> Views { get; set; }
    }

    [Keyless]
    public class KeylessView
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InheritedKey_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ChildEntity> Children { get; set; }
    }

    public class BaseEntity
    {
        public int Id { get; set; }
    }

    public class ChildEntity : BaseEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FluentApiHasKey_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<FluentEntity> FluentEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentEntity>().HasKey(e => e.CustomId);
        }
    }

    public class FluentEntity
    {
        public int CustomId { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithEntityTypeConfiguration_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ConfigEntity> ConfigEntities { get; set; }
    }

    public class ConfigEntity
    {
        public int ConfigId { get; set; }
    }

    public class ConfigEntityConfiguration : IEntityTypeConfiguration<ConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ConfigEntity> builder)
        {
            builder.HasKey(x => x.ConfigId);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_PrivateIdProperty_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<PrivateKeyEntity> PrivateKeys { get; set; }
    }

    public class PrivateKeyEntity
    {
        private int Id { get; set; }
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 40, 48, 51)
            .WithArguments("PrivateKeyEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NavigationPropertyNamedId_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NavIdEntity> NavIdEntities { get; set; }
    }

    public class OtherEntity
    {
        public int Id { get; set; }
    }

    public class NavIdEntity
    {
        public OtherEntity Id { get; set; }  // Navigation property named Id - not a valid key
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 35, 48, 48)
            .WithArguments("NavIdEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_HasNoKeyFluent_ShouldNotTrigger()
    {
        var test = Usings + @"
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
        public EntityTypeBuilder<T> HasNoKey() => this;
    }
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public DbSet<NoKeyView> Views { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NoKeyView>().HasNoKey();
        }
    }

    public class NoKeyView
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_OwnedEntity_ShouldNotTrigger()
    {
        var test = Usings + @"
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
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>().OwnsOne<Address>(c => c.Address);
        }
    }

    public class Customer
    {
        public int Id { get; set; }
        public Address Address { get; set; }
    }

    // Address is an owned type - does NOT need a primary key
    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_HasKeyWithString_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<StringKeyEntity> StringKeyEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StringKeyEntity>().HasKey(""MyKey"");
        }
    }

    public class StringKeyEntity
    {
        public int MyKey { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DbSetAsField_WithoutKey_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NoKeyFieldEntity> NoKeyFields;  // DbSet as field, not property
    }

    public class NoKeyFieldEntity
    {
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 40, 48, 51)
            .WithArguments("NoKeyFieldEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_DbSetAsField_WithKey_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ValidFieldEntity> ValidFields;  // DbSet as field, not property
    }

    public class ValidFieldEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConfigWithoutHasKey_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NoHasKeyConfigEntity> NoHasKeyConfigs { get; set; }
    }

    public class NoHasKeyConfigEntity
    {
        public int SomeProperty { get; set; }
    }

    // Config exists but does NOT define HasKey
    public class NoHasKeyConfigEntityConfiguration : IEntityTypeConfiguration<NoHasKeyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<NoHasKeyConfigEntity> builder)
        {
            // No HasKey call - should trigger warning
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 44, 48, 59)
            .WithArguments("NoHasKeyConfigEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_GuidIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<GuidEntity> GuidEntities { get; set; }
    }

    public class GuidEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NullableIntIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NullableIdEntity> NullableIdEntities { get; set; }
    }

    public class NullableIdEntity
    {
        public int? Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LongIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<LongIdEntity> LongIdEntities { get; set; }
    }

    public class LongIdEntity
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_StringIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<StringIdEntity> StringIdEntities { get; set; }
    }

    public class StringIdEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
