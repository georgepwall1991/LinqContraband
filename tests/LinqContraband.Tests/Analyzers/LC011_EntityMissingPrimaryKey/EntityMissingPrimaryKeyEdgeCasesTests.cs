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
}
