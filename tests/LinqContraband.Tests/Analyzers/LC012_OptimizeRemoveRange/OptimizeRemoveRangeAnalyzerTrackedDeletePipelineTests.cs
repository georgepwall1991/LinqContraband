using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeAnalyzerTrackedDeletePipelineTests
{
    private const string PipelineMock = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } public bool IsDeleted { get; set; } }
    public class Order { public int Id { get; set; } public IEnumerable<OrderLine> Lines { get; set; } }
    public class OrderLine { public int Id { get; set; } public Order Order { get; set; } }
}

namespace Microsoft.EntityFrameworkCore
{
    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }
    public enum DeleteBehavior { ClientSetNull, Restrict, SetNull, Cascade, ClientCascade, NoAction, ClientNoAction }

    public class ChangeTracker
    {
        public IEnumerable<EntityEntry> Entries() => Array.Empty<EntityEntry>();
    }

    public class EntityEntry
    {
        public EntityState State { get; set; }
        public object Entity { get; } = null;
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new EntityTypeBuilder<TEntity>();
        public ModelBuilder ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration) where TEntity : class => this;
        public ModelBuilder ApplyConfigurationsFromAssembly(System.Reflection.Assembly assembly) => this;
        public ModelBuilder ApplyConfigurationsFromAssembly(System.Reflection.Assembly assembly, Func<Type, bool> predicate) => this;
    }

    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public CollectionNavigationBuilder<TEntity, TRelated> HasMany<TRelated>(System.Linq.Expressions.Expression<Func<TEntity, IEnumerable<TRelated>>> navigation) where TRelated : class => new CollectionNavigationBuilder<TEntity, TRelated>();
    }

    public class CollectionNavigationBuilder<TEntity, TRelated> where TEntity : class where TRelated : class
    {
        public ReferenceCollectionBuilder<TEntity, TRelated> WithOne(System.Linq.Expressions.Expression<Func<TRelated, TEntity>> navigation = null) => new ReferenceCollectionBuilder<TEntity, TRelated>();
    }

    public class ReferenceCollectionBuilder<TPrincipal, TDependent> where TPrincipal : class where TDependent : class
    {
        public ReferenceCollectionBuilder<TPrincipal, TDependent> OnDelete(DeleteBehavior deleteBehavior) => this;
    }

    public class DbContextOptionsBuilder
    {
        public DbContextOptionsBuilder AddInterceptors(params object[] interceptors) => this;
    }

    public class DbContext : IDisposable
    {
        public ChangeTracker ChangeTracker { get; } = new ChangeTracker();
        public void Dispose() {}
        public DbSet<TestNamespace.User> Users { get; set; }
        public DbSet<TestNamespace.Order> Orders { get; set; }
        public void RemoveRange(IEnumerable<object> entities) {}
        public virtual int SaveChanges() => 0;
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public void RemoveRange(IEnumerable<T> entities) {}
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}

namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public struct InterceptionResult<TResult>
    {
        public static InterceptionResult<TResult> Empty => default;
    }

    public class DbContextEventData
    {
        public DbContext Context { get; } = null;
    }

    public abstract class SaveChangesInterceptor : ISaveChangesInterceptor
    {
        public virtual InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => result;
    }

    public interface ISaveChangesInterceptor
    {
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection
    {
    }

    public static class EntityFrameworkServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TContext>(
            this IServiceCollection services,
            Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction)
            where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
    }
}
";

    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
";

    [Fact]
    public async Task RemoveRange_WithSaveChangesSoftDelete_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithClientCascade_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var ordersToDelete = db.Orders.Where(o => o.Id > 10);
            db.Orders.RemoveRange(ordersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithAppliedConfigurationClientCascade_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var ordersToDelete = db.Orders.Where(o => o.Id > 10);
            db.Orders.RemoveRange(ordersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithApplyConfigurationsFromAssemblyClientCascade_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var ordersToDelete = db.Orders.Where(o => o.Id > 10);
            db.Orders.RemoveRange(ordersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbSetParameterWithAppliedConfigurationClientCascade_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public class Program
    {
        public void Purge(DbSet<Order> orders)
        {
            orders.RemoveRange(orders.Where(o => o.Id > 10));
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbSetParameterWithSoftDelete_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Purge(DbSet<User> users)
        {
            users.RemoveRange(users.Where(u => u.Id > 10));
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithAddDbContextAddInterceptors_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }

    public class AppDbContext : DbContext
    {
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbSetParameterWithAddDbContextAddInterceptors_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }

    public class AppDbContext : DbContext
    {
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
        }
    }

    public class Program
    {
        public void Purge(DbSet<User> users)
        {
            users.RemoveRange(users.Where(u => u.Id > 10));
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithSiblingDeletedReadAndAddedWrite_StillTriggers()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                if (entry.State == EntityState.Added)
                    ((User)entry.Entity).IsDeleted = false;
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithNegatedDeletedContinueThenConvert_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Deleted)
                    continue;
                entry.State = EntityState.Modified;
                ((User)entry.Entity).IsDeleted = true;
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithParenthesizedIsPatternDeletedThenConvert_StillTriggers()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is (EntityState.Deleted))
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithDeletedAndEntityPatternThenConvert_StillTriggers()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted && entry.Entity is User)
                {
                    entry.State = EntityState.Modified;
                    ((User)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithoutPipeline_StillTriggers()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
