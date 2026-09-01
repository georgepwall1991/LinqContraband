using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    internal const string EfMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public enum EntityState
    {
        Detached = 0,
        Unchanged = 1,
        Deleted = 2,
        Modified = 3,
        Added = 4
    }

    public enum DeleteBehavior
    {
        ClientSetNull,
        Restrict,
        SetNull,
        Cascade,
        ClientCascade,
        NoAction,
        ClientNoAction
    }

    public class ChangeTracker
    {
        public IEnumerable<EntityEntry> Entries() => Array.Empty<EntityEntry>();
        public IEnumerable<EntityEntry<TEntity>> Entries<TEntity>() where TEntity : class => Array.Empty<EntityEntry<TEntity>>();
    }

    public class EntityEntry
    {
        public EntityState State { get; set; }
        public object Entity { get; } = null;
        public PropertyEntry Property(string name) => new PropertyEntry();
    }

    public class EntityEntry<TEntity> where TEntity : class
    {
        public EntityState State { get; set; }
        public TEntity Entity { get; } = null;
        public PropertyEntry Property(string name) => new PropertyEntry();
    }

    public class PropertyEntry
    {
        public object CurrentValue { get; set; }
    }

    public class DbContextOptionsBuilder
    {
        public DbContextOptionsBuilder AddInterceptors(params object[] interceptors) => this;
    }

    public class DbContext
    {
        public ChangeTracker ChangeTracker { get; } = new ChangeTracker();
        public virtual int SaveChanges() => 0;
        public virtual int SaveChanges(bool acceptAllChangesOnSuccess) => 0;
        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
        protected virtual void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public void RemoveRange(IEnumerable<TEntity> entities) { }
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new EntityTypeBuilder<TEntity>();
        public ModelBuilder ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration) where TEntity : class => this;
        public ModelBuilder ApplyConfigurationsFromAssembly(Assembly assembly) => this;
        public ModelBuilder ApplyConfigurationsFromAssembly(Assembly assembly, Func<Type, bool> predicate) => this;
    }

    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public EntityTypeBuilder<TEntity> HasQueryFilter(Expression<Func<TEntity, bool>> filter) => this;
        public EntityTypeBuilder<TEntity> HasKey(Expression<Func<TEntity, object>> keyExpression) => this;
        public CollectionNavigationBuilder<TEntity, TRelated> HasMany<TRelated>(Expression<Func<TEntity, IEnumerable<TRelated>>> navigation) where TRelated : class => new CollectionNavigationBuilder<TEntity, TRelated>();
        public ReferenceNavigationBuilder<TEntity, TRelated> HasOne<TRelated>(Expression<Func<TEntity, TRelated>> navigation) where TRelated : class => new ReferenceNavigationBuilder<TEntity, TRelated>();
    }

    public class CollectionNavigationBuilder<TEntity, TRelated> where TEntity : class where TRelated : class
    {
        public ReferenceCollectionBuilder<TEntity, TRelated> WithOne(Expression<Func<TRelated, TEntity>> navigation = null) => new ReferenceCollectionBuilder<TEntity, TRelated>();
    }

    public class ReferenceNavigationBuilder<TEntity, TRelated> where TEntity : class where TRelated : class
    {
        public ReferenceCollectionBuilder<TRelated, TEntity> WithMany(Expression<Func<TRelated, IEnumerable<TEntity>>> navigation = null) => new ReferenceCollectionBuilder<TRelated, TEntity>();
        public ReferenceReferenceBuilder<TEntity, TRelated> WithOne(Expression<Func<TRelated, TEntity>> navigation = null) => new ReferenceReferenceBuilder<TEntity, TRelated>();
    }

    public class ReferenceCollectionBuilder<TPrincipal, TDependent> where TPrincipal : class where TDependent : class
    {
        public ReferenceCollectionBuilder<TPrincipal, TDependent> OnDelete(DeleteBehavior deleteBehavior) => this;
    }

    public class ReferenceReferenceBuilder<TEntity, TRelated> where TEntity : class where TRelated : class
    {
        public ReferenceReferenceBuilder<TEntity, TRelated> OnDelete(DeleteBehavior deleteBehavior) => this;
        public ReferenceReferenceBuilder<TEntity, TRelated> HasForeignKey<TDependent>(Expression<Func<TDependent, object>> foreignKeyExpression) where TDependent : class => this;
    }

    public abstract class SetPropertyCalls<TSource>
    {
        public SetPropertyCalls<TSource> SetProperty<TProperty>(Expression<Func<TSource, TProperty>> propertyExpression, TProperty valueExpression) => this;
    }

    public static class RelationalQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
        public static Task<int> ExecuteDeleteAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, Expression<Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>>> setPropertyCalls) => 0;
        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source, Expression<Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>>> setPropertyCalls, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
        public static IQueryable<TSource> IgnoreQueryFilters<TSource>(this IQueryable<TSource> source) => source;
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
        public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new ValueTask<InterceptionResult<int>>(result);
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

        public static IServiceCollection AddDbContextPool<TContext>(
            this IServiceCollection services,
            Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction)
            where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

        public static IServiceCollection AddDbContextFactory<TContext>(
            this IServiceCollection services,
            Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction)
            where TContext : Microsoft.EntityFrameworkCore.DbContext => services;

        public static IServiceCollection AddDbContext(
            this IServiceCollection services,
            Type contextType,
            Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction) => services;
    }
}
";

    private static string App(string body) =>
        @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
" + EfMock + @"
namespace TestApp
{
" + body + @"
}";

    [Fact]
    public async Task ExecuteDelete_WithUntypedEntriesSoftDelete_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.Where(u => u.Id > 10).ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_WithUntypedEntriesSoftDelete_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var result = await {|LC047:db.Users.ExecuteDeleteAsync()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithTypedEntriesUserOnly_ShouldTriggerForUser()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class Order
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var users = {|LC047:db.Users.ExecuteDelete()|};
            var orders = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithShadowPropertyCurrentValue_ShouldTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entry.Property(""IsDeleted"").CurrentValue = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Set<User>().ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHelperOnSameContext_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            ConvertDeletes();
            return base.SaveChanges();
        }

        private void ConvertDeletes()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithRegisteredInterceptor_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(new SoftDeleteInterceptor());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithUnregisteredInterceptor_ShouldNotTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Users.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithClientCascadeOnPrincipal_ShouldTrigger()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> Lines { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.Where(o => o.Id > 0).ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithClientSetNullOnPrincipal_ShouldTrigger()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientSetNull);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDatabaseCascade_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_OnDependentWithClientCascade_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> Lines { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Lines.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHasQueryFilterOnly_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Users.Where(u => u.Id > 10).ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_LookalikeExtension_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User { public int Id { get; set; } }

    public static class QueryExtensions
    {
        public static int ExecuteDelete<T>(this IQueryable<T> source) => 0;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Modified;
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = QueryExtensions.ExecuteDelete(db.Users);
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_OnContextWithoutPipeline_ShouldNotTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class SoftDeleteContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class ReportingContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Run(ReportingContext db)
        {
            var result = db.Users.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_OnBaseDbContextParameter_ShouldNotTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Set<User>().ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_WithSoftDeletePipeline_ShouldNotTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
        public string Name { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Users.ExecuteUpdate(s => s.SetProperty(u => u.Name, ""x""));
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHasOneClientCascade_ShouldTriggerOnPrincipal()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> Lines { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderLine>()
                .HasOne(l => l.Order)
                .WithMany(o => o.Lines)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var orders = {|LC047:db.Orders.ExecuteDelete()|};
            var lines = db.Lines.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithBaseContextConversion_ShouldTriggerOnDerivedContext()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public abstract class AuditedDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class AppDbContext : AuditedDbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
