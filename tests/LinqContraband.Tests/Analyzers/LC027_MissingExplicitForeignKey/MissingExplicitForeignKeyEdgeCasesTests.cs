using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC027_MissingExplicitForeignKey;

public class MissingExplicitForeignKeyEdgeCasesTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public class DbContext
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new EntityTypeBuilder<TEntity>();
        public void ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration) where TEntity : class { }
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public ReferenceNavigationBuilder<TEntity, TRelated> HasOne<TRelated>(Expression<Func<TEntity, TRelated>> navigationExpression = null) where TRelated : class => new ReferenceNavigationBuilder<TEntity, TRelated>();
        public OwnedNavigationBuilder<TEntity, TOwned> OwnsOne<TOwned>(Expression<Func<TEntity, TOwned>> navigationExpression) where TOwned : class => new OwnedNavigationBuilder<TEntity, TOwned>();
    }

    public class ReferenceNavigationBuilder<TEntity, TRelated>
        where TEntity : class
        where TRelated : class
    {
        public ReferenceCollectionBuilder<TEntity, TRelated> WithMany(Expression<Func<TRelated, IEnumerable<TEntity>>> navigationExpression = null) => new ReferenceCollectionBuilder<TEntity, TRelated>();
    }

    public class ReferenceCollectionBuilder<TEntity, TRelated>
        where TEntity : class
        where TRelated : class
    {
        public ReferenceCollectionBuilder<TEntity, TRelated> HasForeignKey<TDependent>(Expression<Func<TDependent, object>> foreignKeyExpression) where TDependent : class => this;
    }

    public class OwnedNavigationBuilder<TEntity, TOwned>
        where TEntity : class
        where TOwned : class
    {
    }
}
";

    [Fact]
    public async Task Navigation_WithOnModelCreatingHasForeignKey_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer Customer { get; set; }
        public int CustomerId { get; set; }
    }

    public class Customer { public int Id { get; set; } public System.Collections.Generic.List<Order> Orders { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey<Order>(o => o.CustomerId);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_ToOwnedType_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Address Address { get; set; }
    }

    public class Address
    {
        public string Street { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().OwnsOne(o => o.Address);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithEntityTypeConfigurationHasForeignKey_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer Customer { get; set; }
        public int CustomerId { get; set; }
    }

    public class Customer { public int Id { get; set; } public System.Collections.Generic.List<Order> Orders { get; set; } }

    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
        {
            builder.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey<Order>(o => o.CustomerId);
        }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
