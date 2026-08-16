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

    public class ModelBuilder
    {
        public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new EntityTypeBuilder<TEntity>();
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
";

    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
using Microsoft.EntityFrameworkCore;
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
