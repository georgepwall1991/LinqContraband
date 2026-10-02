using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC052_NonDeterministicModelData.NonDeterministicModelDataAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC052_NonDeterministicModelData;

public partial class NonDeterministicModelDataTests
{
    internal const string EfCoreMock = @"
using System;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public virtual DataBuilder<TEntity> HasData(params TEntity[] data) => null;
        public virtual DataBuilder<TEntity> HasData(params object[] data) => null;
        public virtual PropertyBuilder<TProperty> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression) => null;
        public virtual OwnedNavigationBuilder<TEntity, TDependent> OwnsOne<TDependent>(Expression<Func<TEntity, TDependent>> navigationExpression) where TDependent : class => null;
    }

    public class OwnedNavigationBuilder<TOwner, TDependent> where TDependent : class
    {
        public virtual DataBuilder<TDependent> HasData(params object[] data) => null;
    }

    public class DataBuilder<TEntity> { }

    public class PropertyBuilder<TProperty>
    {
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class ModelBuilder
    {
        public virtual EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => null;
    }

    public static class RelationalPropertyBuilderExtensions
    {
        public static PropertyBuilder<TProperty> HasDefaultValue<TProperty>(this PropertyBuilder<TProperty> propertyBuilder, object value) => propertyBuilder;
        public static PropertyBuilder<TProperty> HasDefaultValueSql<TProperty>(this PropertyBuilder<TProperty> propertyBuilder, string sql) => propertyBuilder;
    }
}

namespace TestApp
{
    public class Blog
    {
        public int Id { get; set; }
        public Guid Key { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Address Address { get; set; }
    }

    public class Address { public int BlogId { get; set; } public string City { get; set; } }
}
";

    internal static string Wrap(string body, string members = "") => EfCoreMock + @"
namespace TestApp
{
    using System;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class Model
    {
" + members + @"
        public void OnModelCreating(ModelBuilder modelBuilder)
        {
" + body + @"
        }
    }
}";

    [Fact]
    public async Task HasData_WithNow_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = {|LC052:DateTime.Now|} });"));
    }

    [Fact]
    public async Task HasData_ReportsEveryValue()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(
                new Blog { Id = 1, Key = {|LC052:Guid.NewGuid()|}, CreatedAt = {|LC052:DateTime.UtcNow|} },
                new Blog { Id = 2, UpdatedAt = {|LC052:DateTimeOffset.UtcNow|}, CreatedAt = {|LC052:DateTime.Today|} });"));
    }

    [Fact]
    public async Task Message_NamesValueMethodAndAdvice()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = {|#0:DateTime.UtcNow|} });
            modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValue({|#1:DateTime.UtcNow|});"),
            VerifyCS.Diagnostic("LC052").WithLocation(0).WithArguments("DateTime.UtcNow", "HasData", "Seed a fixed value instead"),
            VerifyCS.Diagnostic("LC052").WithLocation(1).WithArguments(
                "DateTime.UtcNow", "HasDefaultValue", "Use HasDefaultValueSql with the database's current-time or new-id function instead"));
    }

    [Fact]
    public async Task AnonymousSeedObject_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new { Id = 1, CreatedAt = {|LC052:DateTime.Now|} });"));
    }

    [Fact]
    public async Task OwnedTypeSeed_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().OwnsOne(b => b.Address).HasData(new { BlogId = 1, City = {|LC052:Guid.NewGuid()|}.ToString() });"));
    }

    [Fact]
    public async Task EntityTypeConfiguration_Reports()
    {
        var code = EfCoreMock + @"
namespace TestApp
{
    using System;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class BlogConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder)
        {
            builder.HasData(new Blog { Id = 1, CreatedAt = {|LC052:DateTime.UtcNow|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Fact]
    public async Task ValueThroughLocal_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var now = DateTime.UtcNow;
            var seed = new[] { new Blog { Id = 1, CreatedAt = now } };
            modelBuilder.Entity<Blog>().HasData({|LC052:seed|});
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 2, CreatedAt = {|LC052:now|} });"));
    }

    [Fact]
    public async Task ValueThroughStaticReadonlyField_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = {|LC052:SeedDate|} });",
            @"        private static readonly DateTime SeedDate = DateTime.UtcNow;
"));
    }

    [Fact]
    public async Task FixedValues_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog
            {
                Id = 1,
                Key = new Guid(""8c1d2f6e-7a55-4c43-9a2a-1f8b3e6d9c01""),
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValueSql(""CURRENT_TIMESTAMP"");
            modelBuilder.Entity<Blog>().Property(b => b.Id).HasDefaultValue(0);"));
    }

    [Fact]
    public async Task ReassignedLocal_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var created = DateTime.UtcNow;
            created = new DateTime(2024, 1, 1);
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = created });"));
    }

    [Fact]
    public async Task InstanceReadonlyFieldAndMutableStatic_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = _created });
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 2, CreatedAt = Mutable });",
            @"        private readonly DateTime _created = new DateTime(2024, 1, 1);
        private static DateTime Mutable = DateTime.UtcNow;
"));
    }

    [Fact]
    public async Task NowOutsideModelBuilding_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var blog = new Blog { Id = 1, CreatedAt = DateTime.UtcNow, Key = Guid.NewGuid() };
            Console.WriteLine(blog.CreatedAt);"));
    }

    [Fact]
    public async Task UnrelatedHasDataMethod_StaysQuiet()
    {
        var code = EfCoreMock + @"
namespace TestApp
{
    using System;

    public class Fixture
    {
        public void HasData(params object[] data) { }

        public void Run() => HasData(new Blog { CreatedAt = DateTime.UtcNow });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}
