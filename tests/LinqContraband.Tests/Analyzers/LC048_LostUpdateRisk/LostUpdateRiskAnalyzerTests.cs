using LinqContraband.Analyzers.LC048_LostUpdateRisk;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace LinqContraband.Tests.Analyzers.LC048_LostUpdateRisk;

public sealed class LostUpdateRiskAnalyzerTests
{
    private const string EfCoreMock = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DatabaseFacade Database { get; } = new DatabaseFacade();
        public ChangeTracking.ChangeTracker ChangeTracker { get; } = new ChangeTracking.ChangeTracker();
        public DbSet<T> Set<T>() where T : class => null;
        public DbSet<T> Set<T>(string name) where T : class => null;
        public virtual T Find<T>(params object[] keys) where T : class => default;
        public virtual ValueTask<T> FindAsync<T>(params object[] keys) where T : class =>
            new ValueTask<T>(default(T));
        public virtual ValueTask<T> FindAsync<T>(
            object[] keys,
            CancellationToken cancellationToken) where T : class =>
            new ValueTask<T>(default(T));
        public virtual ChangeTracking.EntityEntry<T> Update<T>(T entity) where T : class =>
            new ChangeTracking.EntityEntry<T>();
        public virtual ChangeTracking.EntityEntry<T> Attach<T>(T entity) where T : class =>
            new ChangeTracking.EntityEntry<T>();
        public virtual void UpdateRange(params object[] entities) { }
        public virtual void UpdateRange(IEnumerable<object> entities) { }
        public virtual void AttachRange(params object[] entities) { }
        public virtual void AttachRange(IEnumerable<object> entities) { }
        public virtual ChangeTracking.EntityEntry<T> Remove<T>(T entity) where T : class =>
            new ChangeTracking.EntityEntry<T>();
        public virtual void RemoveRange(params object[] entities) { }
        public virtual void RemoveRange(IEnumerable<object> entities) { }
        public virtual ChangeTracking.EntityEntry<T> Entry<T>(T entity) where T : class =>
            new ChangeTracking.EntityEntry<T>();
        public virtual int SaveChanges() => 0;
        public virtual int SaveChanges(bool acceptAllChangesOnSuccess) => 0;
        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
        public virtual Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
        protected virtual void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
    }

    public enum EntityState
    {
        Added,
        Modified,
        Unchanged,
        Detached,
        Deleted,
    }

    public enum QueryTrackingBehavior
    {
        TrackAll,
        NoTracking,
        NoTrackingWithIdentityResolution,
    }

    public enum ChangeTrackingStrategy
    {
        Snapshot,
        ChangedNotifications,
        ChangingAndChangedNotifications,
        ChangingAndChangedNotificationsWithOriginalValues,
    }


    public class DatabaseFacade
    {
        public Storage.IDbContextTransaction BeginTransaction() =>
            new Storage.IDbContextTransaction();
        public Task<Storage.IDbContextTransaction> BeginTransactionAsync() =>
            Task.FromResult(new Storage.IDbContextTransaction());
        public ValueTask<Storage.IDbContextTransaction> BeginTransactionAsync(bool valueTask) =>
            new ValueTask<Storage.IDbContextTransaction>(new Storage.IDbContextTransaction());
        public TransactionResultLookalike BeginTransactionAsync(int resultLookalike) =>
            new TransactionResultLookalike();
        public TransactionAwaitableLookalike BeginTransactionAsync(string awaiterLookalike) =>
            new TransactionAwaitableLookalike();
    }
    public sealed class TransactionResultLookalike
    {
        public Storage.IDbContextTransaction Result => new Storage.IDbContextTransaction();
    }

    public sealed class TransactionAwaitableLookalike
    {
        public TransactionAwaiterLookalike GetAwaiter() => new TransactionAwaiterLookalike();
    }

    public sealed class TransactionAwaiterLookalike
    {
        public Storage.IDbContextTransaction GetResult() =>
            new Storage.IDbContextTransaction();
    }


    public static class RelationalDatabaseFacadeExtensions
    {
        public static Storage.IDbContextTransaction UseTransaction(
            this DatabaseFacade database,
            System.Data.Common.DbTransaction transaction) =>
                new Storage.IDbContextTransaction();
        public static Task<Storage.IDbContextTransaction> UseTransactionAsync(
            this DatabaseFacade database,
            System.Data.Common.DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
                Task.FromResult(new Storage.IDbContextTransaction());
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public virtual T Find(params object[] keys) => default;
        public virtual ValueTask<T> FindAsync(params object[] keys) =>
            new ValueTask<T>(default(T));
        public virtual ValueTask<T> FindAsync(
            object[] keys,
            CancellationToken cancellationToken) => new ValueTask<T>(default(T));
        public virtual ChangeTracking.EntityEntry<T> Update(T entity) =>
            new ChangeTracking.EntityEntry<T>();
        public virtual void UpdateRange(params T[] entities) { }
        public virtual void UpdateRange(IEnumerable<T> entities) { }
        public virtual ChangeTracking.EntityEntry<T> Attach(T entity) =>
            new ChangeTracking.EntityEntry<T>();
        public virtual void AttachRange(params T[] entities) { }
        public virtual void AttachRange(IEnumerable<T> entities) { }
        public virtual ChangeTracking.EntityEntry<T> Remove(T entity) =>
            new ChangeTracking.EntityEntry<T>();
        public virtual void RemoveRange(params T[] entities) { }
        public virtual void RemoveRange(IEnumerable<T> entities) { }
        public Type ElementType => typeof(T);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public class DbContextOptionsBuilder { }

    public static class DbContextOptionsBuilderExtensions
    {
        public static DbContextOptionsBuilder UseQueryTrackingBehavior(
            this DbContextOptionsBuilder builder,
            QueryTrackingBehavior behavior) => builder;
        public static DbContextOptionsBuilder UseChangeTrackingProxies(
            this DbContextOptionsBuilder builder,
            bool useChangeTrackingProxies = true) => builder;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) where T : class => source;
        public static IQueryable<T> AsTracking<T>(
            this IQueryable<T> source,
            QueryTrackingBehavior behavior) where T : class => source;
        public static Task<T> FirstAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public static Task<T> SingleOrDefaultAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public static Task<T> LastAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(new List<T>());
        public static int ExecuteUpdate<T>(this IQueryable<T> source, Expression<Func<T, T>> update) => 0;
    }

    public class ModelBuilder
    {
        public Metadata.Builders.EntityTypeBuilder<T> Entity<T>() where T : class => null;
        public void Entity<T>(Action<Metadata.Builders.EntityTypeBuilder<T>> buildAction)
            where T : class => buildAction(new Metadata.Builders.EntityTypeBuilder<T>());
        public ModelBuilder HasChangeTrackingStrategy(ChangeTrackingStrategy strategy) => this;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class KeylessAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class PrimaryKeyAttribute : Attribute
    {
        public PrimaryKeyAttribute(string propertyName, params string[] additionalPropertyNames) { }
    }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    using System;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;

    public class EntityEntry
    {
        public EntityState State { get; set; }
        public void Reload() { }
        public void Reload(int lookalikeArgument) { }
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public PropertyEntry Property(string propertyName) => new PropertyEntry();
        public PropertyEntry Property(int lookalikeArgument) => new PropertyEntry();
    }

    public class EntityEntry<T> : EntityEntry where T : class
    {
        public PropertyEntry<T, TProperty> Property<TProperty>(
            Expression<Func<T, TProperty>> propertyExpression) => new PropertyEntry<T, TProperty>();
    }

    public class PropertyEntry
    {
        public bool IsModified { get; set; }
    }

    public sealed class PropertyEntry<TEntity, TProperty> : PropertyEntry
        where TEntity : class
    {
    }

    public sealed class ChangeTracker
    {
        public void Clear() { }
        public void Clear(int lookalikeArgument) { }
        public void DetectChanges() { }
        public void AcceptAllChanges() { }
        public void AcceptAllChanges(int lookalikeArgument) { }
        public bool AutoDetectChangesEnabled { get; set; }
        public QueryTrackingBehavior QueryTrackingBehavior { get; set; }
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    using System;

    public sealed class IDbContextTransaction : IDisposable
    {
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
        public System.Threading.Tasks.Task CommitAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task RollbackAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using Microsoft.EntityFrameworkCore;

    public interface IServiceCollection { }

    public static class EntityFrameworkServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;

        public static IServiceCollection AddDbContext<TService, TImplementation>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> optionsAction)
            where TImplementation : DbContext, TService => services;

        public static IServiceCollection AddDbContext<TContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder, DbContextOptionsBuilder> ambiguousAction)
            where TContext : DbContext => services;

        public static IServiceCollection AddDbContextPool<TContext>(
            this IServiceCollection services,
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;

        public static IServiceCollection AddDbContextPool<TService, TImplementation>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> optionsAction)
            where TImplementation : DbContext, TService => services;

        public static IServiceCollection AddDbContextFactory<TContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;

        public static IServiceCollection AddDbContextFactory<TContext, TFactory>(
            this IServiceCollection services,
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;

        public static IServiceCollection AddPooledDbContextFactory<TContext>(
            this IServiceCollection services,
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;
    }
}


namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{

    public class EntityTypeBuilder<T> where T : class
    {
        public PropertyBuilder<TProperty> Property<TProperty>(Expression<Func<T, TProperty>> property) => null;
        public PropertyBuilder<object> Property(string propertyName) => null;
        public PropertyBuilder<TProperty> Property<TProperty>(string propertyName) => null;
        public EntityTypeBuilder<T> HasNoKey() => this;
        public EntityTypeBuilder<T> HasKey(Expression<Func<T, object>> key) => this;
        public EntityTypeBuilder<T> HasKey(params string[] propertyNames) => this;
        public KeyBuilder<T> HasAlternateKey(Expression<Func<T, object>> key) => null;
        public KeyBuilder<T> HasAlternateKey(params string[] propertyNames) => null;
        public EntityTypeBuilder<T> HasChangeTrackingStrategy(
            Microsoft.EntityFrameworkCore.ChangeTrackingStrategy strategy) => this;
        public EntityTypeBuilder<T> Ignore<TProperty>(
            Expression<Func<T, TProperty>> property) => this;
        public EntityTypeBuilder<T> Ignore(string propertyName) => this;
    }

    public class KeyBuilder<T> where T : class
    {
    }

    public class PropertyBuilder<TProperty>
    {
        public PropertyBuilder<TProperty> IsConcurrencyToken(bool concurrencyToken = true) => this;
        public PropertyBuilder<TProperty> IsRowVersion() => this;
        public PropertyBuilder<TProperty> ValueGeneratedOnAddOrUpdate() => this;
        public PropertyBuilder<TProperty> ValueGeneratedNever() => this;
        public PropertyBuilder<TProperty> ValueGeneratedOnAdd() => this;
        public PropertyBuilder<TProperty> ValueGeneratedOnUpdate() => this;
    }
}
namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalPropertyBuilderExtensions
    {
        public static Metadata.Builders.PropertyBuilder<TProperty> HasComputedColumnSql<TProperty>(
            this Metadata.Builders.PropertyBuilder<TProperty> builder,
            string sql) => builder;
    }
}

""";

    private const string Domain = """
namespace Test
{
    using Microsoft.EntityFrameworkCore;
    public sealed class Order
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public int Status { get; set; }
        public string Name { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }
""";

    [Fact]
    public async Task SyncCompoundAssignmentReportsMutationWithSaveLocation()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.Single(x => x.Id == 1);
            {|LC048:order.Quantity|} += 2;
            db.SaveChanges();
        }

        public void InlineTerminal(AppDbContext db)
        {
            {|LC048:db.Orders.First().Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task AsyncIncrementAndContextSetReport()
    {
        await VerifyAsync(
            Domain
                + """
}

namespace Microsoft.EntityFrameworkCore.Custom
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<T> FirstAsync<T>(
            this System.Linq.IQueryable<T> source,
            System.Threading.CancellationToken cancellationToken = default) =>
                System.Threading.Tasks.Task.FromResult(default(T));
    }
}

namespace Test
{
    using Microsoft.EntityFrameworkCore;
    public sealed class Service
    {
        public async Task Update(AppDbContext db)
        {
            var order = await db.Set<Order>().FirstAsync();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async Task SameNameExtensionInEfSubnamespaceIsNotATerminal(
            AppDbContext db)
        {
            var order = await Microsoft.EntityFrameworkCore.Custom
                .EntityFrameworkQueryableExtensions.FirstAsync(db.Orders);
            order.Quantity++;
            await db.SaveChangesAsync();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task SelfReadAssignmentReports()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = order.Quantity + 1;
            db.SaveChanges();
        }

        public void StableLocal(AppDbContext db, int amount)
        {
            var order = db.Orders.First();
            var old = order.Quantity;
            {|LC048:order.Quantity|} = old + amount;
            db.SaveChanges();
        }

        public void StableAliasAndConversion(AppDbContext db, int amount)
        {
            var order = db.Orders.First();
            var entityAlias = order;
            var old = entityAlias.Quantity;
            long converted = old;
            var alias = converted;
            {|LC048:order.Quantity|} = (int)alias + amount;
            db.SaveChanges();
        }

        public void StableDerivedAndConditionalLocals(
            AppDbContext db,
            int amount,
            bool chooseFormula)
        {
            var order = db.Orders.First();
            var next = order.Quantity + amount;
            {|LC048:order.Quantity|} = next;
            db.SaveChanges();

            var second = db.Orders.First();
            var fromEitherBranch = chooseFormula
                ? second.Quantity + amount
                : second.Quantity - amount;
            {|LC048:second.Quantity|} = fromEitherBranch;
            db.SaveChanges();

            var third = db.Orders.First();
            var fromCondition = third.Quantity > amount ? 1 : 0;
            {|LC048:third.Quantity|} = fromCondition;
            db.SaveChanges();
        }

        public void SnapshotSelfAssignmentIsNotAMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = order.Quantity;
            db.SaveChanges();
        }

        public void ConvertedAndOperatorSelfValuesRemainMutations(AppDbContext db)
        {
            var converted = db.Orders.First();
            {|LC048:converted.Quantity|} = (int)(long)converted.Quantity;
            db.SaveChanges();

            var operated = db.Orders.First();
            {|LC048:operated.Quantity|} = +operated.Quantity;
            db.SaveChanges();
        }

        public void ExplicitPropertyPersistencePersistsSetter(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = order.Quantity;
            db.Entry(order).Property(x => x.Quantity).IsModified = true;
            db.SaveChanges();
        }

        public void ExplicitEntityPersistencePersistsSetter(AppDbContext db)
        {
            var stateOrder = db.Orders.First();
            {|LC048:stateOrder.Quantity|} = stateOrder.Quantity;
            db.Entry(stateOrder).State = EntityState.Modified;
            db.SaveChanges();

            var updatedOrder = db.Orders.First();
            {|LC048:updatedOrder.Quantity|} = updatedOrder.Quantity;
            db.Update(updatedOrder);
            db.SaveChanges();
        }

        public void AmbiguousLocalsStayQuiet(AppDbContext db, bool chooseLoaded)
        {
            var order = db.Orders.First();
            var other = db.Orders.First();

            var reassigned = order.Quantity;
            reassigned = 0;
            order.Quantity = reassigned + 1;

            var escaped = order.Quantity;
            Reset(ref escaped);
            order.Quantity = escaped + 1;

            var crossEntity = other.Quantity;
            order.Quantity = crossEntity + 1;

            var crossProperty = order.Status;
            order.Quantity = crossProperty + 1;

            var conditional = chooseLoaded ? order.Quantity : 0;
            order.Quantity = conditional + 1;
            db.SaveChanges();

            var invocationAmbiguous = Transform(order.Quantity);
            order.Quantity = invocationAmbiguous;
        }

        public void CapturedBeforeBlindResetReports(AppDbContext db)
        {
            var order = db.Orders.First();
            var old = order.Quantity;
            order.Quantity = 0;
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void CapturedBeforeReloadReports(AppDbContext db)
        {
            var order = db.Orders.First();
            var old = order.Quantity;
            db.Entry(order).Reload();
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void CapturedBeforeDetachAndReattachReports(AppDbContext db)
        {
            var order = db.Orders.First();
            var old = order.Quantity;
            db.Entry(order).State = EntityState.Detached;
            db.Attach(order);
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void CapturedBeforeUnchangedResetReports(AppDbContext db)
        {
            var order = db.Orders.First();
            var old = order.Quantity;
            db.Entry(order).State = EntityState.Unchanged;
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void LocalReadAfterBlindResetStaysQuiet(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            var old = order.Quantity;
            order.Quantity = old + 1;
            db.SaveChanges();

            var derived = old + 1;
            order.Quantity = derived;
            db.SaveChanges();
        }

        public void LocalReadAfterReloadStaysQuiet(AppDbContext db)
        {
            var order = db.Orders.First();
            db.Entry(order).Reload();
            var old = order.Quantity;
            order.Quantity = old + 1;
            db.SaveChanges();
        }

        public void ConditionalBlindResetBeforeReadReports(AppDbContext db, bool reset)
        {
            var order = db.Orders.First();
            if (reset)
                order.Quantity = 0;
            var old = order.Quantity;
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void MutuallyExclusiveBlindResetBeforeReadReports(AppDbContext db, bool reset)
        {
            var order = db.Orders.First();
            if (reset)
                order.Quantity = 0;
            if (!reset)
            {
                var old = order.Quantity;
                {|LC048:order.Quantity|} = old + 1;
                db.SaveChanges();
            }
        }

        public void ConditionalReloadBeforeReadReports(AppDbContext db, bool reload)
        {
            var order = db.Orders.First();
            if (reload)
                db.Entry(order).Reload();
            var old = order.Quantity;
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void NonmatchingResetsBeforeReadReport(AppDbContext db)
        {
            var order = db.Orders.First();
            var other = db.Orders.First();
            other.Quantity = 0;
            order.Status = 0;
            db.Entry(other).Reload();
            var old = order.Quantity;
            {|LC048:order.Quantity|} = old + 1;
            db.SaveChanges();
        }

        public void LoadedLocalNotUsedInFinalWriteStaysQuiet(AppDbContext db, int amount)
        {
            var order = db.Orders.First();
            var unused = order.Quantity;
            order.Quantity = amount;
            db.SaveChanges();
        }

        private static void Reset(ref int value) => value = 0;
        private static int Transform(int value) => value;
    }
}
"""
        );
    }

    [Fact]
    public async Task GuardedStateTransitionReports()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.First();
            if (order.Status == 1)
            {
                {|LC048:order.Status|} = 2;
            }
            db.SaveChanges();
        }

        public void ReturnGuard(AppDbContext db)
        {
            var order = db.Orders.First();
            if (order.Status != 1)
                return;

            {|LC048:order.Status|} = 2;
            db.SaveChanges();
        }

        public void StableLocalThrowGuard(AppDbContext db)
        {
            var order = db.Orders.First();
            var loadedStatus = order.Status;
            if (loadedStatus is not 1)
                throw new InvalidOperationException();

            {|LC048:order.Status|} = 2;
            db.SaveChanges();
        }

        public void ContinueGuard(AppDbContext db)
        {
            foreach (var ignored in new[] { 0 })
            {
                var order = db.Orders.First();
                if (order.Status != 1)
                    continue;

                {|LC048:order.Status|} = 2;
                db.SaveChanges();
            }
        }

        public void ConditionalFallthroughIsNotProof(AppDbContext db, bool stop)
        {
            var order = db.Orders.First();
            if (order.Status != 1 && stop)
                return;

            order.Status = 2;
            db.SaveChanges();
        }

        public void UnrelatedEarlyExitIsNotProof(AppDbContext db, bool stop)
        {
            var order = db.Orders.First();
            if (stop)
                return;

            order.Status = 2;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task EntityAndContextAliasesReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var context = db;
            var order = context.Orders.First();
            var alias = order;
            {|LC048:alias.Quantity|}--;
            context.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task PrivateSyncAndObservedAsyncHelpersReportWhenSameFileAndProven()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Sync(AppDbContext db)
        {
            var order = db.Orders.First();
            Adjust(order);
            Persist(db);
        }

        public async Task AwaitedMutationOnly(AppDbContext db)
        {
            var order = db.Orders.First();
            await AdjustAsync(order);
            await db.SaveChangesAsync();
        }

        public async Task AwaitedSaveOnlyWithConfigureAwait(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await PersistAsync(db).ConfigureAwait(false);
        }

        public async Task AwaitedCombinedWithConfigureAwait(AppDbContext db)
        {
            var order = db.Orders.First();
            await AdjustAndPersistAsync(order, db).ConfigureAwait(false);
        }

        public async Task UnobservedInvocationsStayQuiet(AppDbContext db)
        {
            var first = db.Orders.First();
            AdjustAndPersistAsync(first, db);

            var second = db.Orders.First();
            _ = AdjustAndPersistAsync(second, db);

            var third = db.Orders.First();
            var stored = AdjustAndPersistAsync(third, db);
            await Task.CompletedTask;
        }

        public async Task ConditionalWrapperStaysQuiet(AppDbContext db, bool update)
        {
            var order = db.Orders.First();
            await (update
                ? AdjustAndPersistAsync(order, db)
                : Task.CompletedTask);
        }

        public async Task FalseSaveConditionStaysQuiet(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            await PersistWhenAsync(db, false);
        }

        public async Task WrongContextStaysQuiet(AppDbContext db, AppDbContext audit)
        {
            var order = db.Orders.First();
            await AdjustAndPersistAsync(order, audit);
        }

        public async Task NonprivateAndAmbiguousWrappersStayQuiet(AppDbContext db)
        {
            var first = db.Orders.First();
            await PublicAdjustAndPersistAsync(first, db);

            var second = db.Orders.First();
            await Task.WhenAll(AdjustAndPersistAsync(second, db));
        }

        public void AsyncVoidStaysQuiet(AppDbContext db)
        {
            var order = db.Orders.First();
            AdjustAndPersistVoid(order, db);
        }

        private static void Adjust(Order order)
        {
            {|LC048:order.Quantity|}++;
        }

        private static void Persist(AppDbContext db) => db.SaveChanges();

        private static async Task AdjustAsync(Order order)
        {
            await Task.Yield();
            {|LC048:order.Quantity|}++;
        }

        private static async Task PersistAsync(AppDbContext db)
        {
            await db.SaveChangesAsync();
        }

        private static async Task AdjustAndPersistAsync(Order order, AppDbContext db)
        {
            await Task.Yield();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        private static async Task PersistWhenAsync(AppDbContext db, bool save)
        {
            await Task.Yield();
            if (save)
                await db.SaveChangesAsync();
        }

        public static async Task PublicAdjustAndPersistAsync(Order order, AppDbContext db)
        {
            await Task.Yield();
            order.Quantity++;
            await db.SaveChangesAsync();
        }


        private static async void AdjustAndPersistVoid(Order order, AppDbContext db)
        {
            await Task.Yield();
            order.Quantity++;
            await db.SaveChangesAsync();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task BlindWriteAndUnsavedMutationDoNotReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Blind(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Name = "replacement";
            db.SaveChanges();
        }

        public void Unsaved(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task NoTrackingAndDifferentContextDoNotReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void NoTracking(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void IdentityResolutionNoTracking(AppDbContext db)
        {
            var order = db.Orders.AsNoTrackingWithIdentityResolution().First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void OtherContext(AppDbContext readDb, AppDbContext writeDb)
        {
            var order = readDb.Orders.First();
            order.Quantity++;
            writeDb.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConcurrencyAttributesDoNotReport()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;
    using System.ComponentModel.DataAnnotations;
    public sealed class VersionedOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        [Timestamp] public byte[] Version { get; set; }
    }
    public sealed class CheckedOrder
    {
        public int Id { get; set; }
        [ConcurrencyCheck] public int Quantity { get; set; }
    }
    public sealed class StaticTimestampOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        [Timestamp] public static byte[] Version { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<VersionedOrder> Versioned { get; set; }
        public DbSet<CheckedOrder> Checked { get; set; }
        public DbSet<StaticTimestampOrder> StaticTimestamp { get; set; }
    }
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var first = db.Versioned.First();
            first.Quantity++;
            var second = db.Checked.First();
            second.Quantity++;
            var third = db.StaticTimestamp.First();
            {|LC048:third.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Theory]
    [InlineData("IsConcurrencyToken")]
    [InlineData("IsRowVersion")]
    public async Task DirectFluentConcurrencyConfigurationDoesNotReport(string configuration)
    {
        await VerifyAsync(
            Domain
                + $$"""
    public sealed class ConfiguredDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).{{configuration}}();
        }
    }

    public sealed class Service
    {
        public void Update(ConfiguredDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task DirectTransactionsCorrelateContextOrderPathAndLifetime()
    {
        await VerifyAsync(
            Domain
                + """

}

namespace Microsoft.EntityFrameworkCore.Custom
{
    public static class SameNameTransactionExtensions
    {
        public static Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
            BeginTransaction(this Microsoft.EntityFrameworkCore.DatabaseFacade database) =>
                new Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction();

        public static System.Threading.Tasks.Task<
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>
            BeginTransactionAsync(
                this Microsoft.EntityFrameworkCore.DatabaseFacade database,
                System.Threading.CancellationToken cancellationToken = default) =>
                System.Threading.Tasks.Task.FromResult(
                    new Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction());
    }
}

namespace Test
{
    using System.Linq;
    using Microsoft.EntityFrameworkCore;
#nullable enable annotations
    public sealed class Service
    {
        public void Atomic(AppDbContext db)
        {
            db.Orders.Where(x => x.Id == 1).ExecuteUpdate(x => x);
        }

        public async System.Threading.Tasks.Task SameNameExtensionsDoNotProtect(
            AppDbContext db)
        {
            Microsoft.EntityFrameworkCore.Custom.SameNameTransactionExtensions
                .BeginTransaction(db.Database);
            var synchronous = db.Orders.First();
            {|LC048:synchronous.Quantity|}++;
            db.SaveChanges();

            await Microsoft.EntityFrameworkCore.Custom.SameNameTransactionExtensions
                .BeginTransactionAsync(db.Database);
            var asynchronous = db.Orders.First();
            {|LC048:asynchronous.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public void BeforeRead(AppDbContext db)
        {
            db.Database.BeginTransaction();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void AfterRead(AppDbContext db)
        {
            var order = db.Orders.First();
            using var transaction = db.Database.BeginTransaction();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BeforeSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Database.BeginTransaction();
            db.SaveChanges();
        }

        public void Unrelated(AppDbContext db, AppDbContext audit)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            audit.Database.BeginTransaction();
            db.SaveChanges();
        }

        public void AfterSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
            db.Database.BeginTransaction();
        }

        public void Conditional(AppDbContext db, bool useTransaction)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            if (useTransaction)
                db.Database.BeginTransaction();
            db.SaveChanges();
        }

        public void Correlated(AppDbContext db, bool useTransaction)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            if (useTransaction)
            {
                db.Database.BeginTransaction();
                db.SaveChanges();
            }
        }

        public void Unreachable(AppDbContext db)
        {
            goto Save;
            db.Database.BeginTransaction();

        Save:
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DisposedBeforeMutation(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            transaction.Dispose();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void CommittedBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            transaction.Commit();
            db.SaveChanges();
        }

        public void RolledBackBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            transaction.Rollback();
            db.SaveChanges();
        }

        public void CompletedUsingScope(AppDbContext db)
        {
            var order = db.Orders.First();
            using (var transaction = db.Database.BeginTransaction())
            {
                {|LC048:order.Quantity|}++;
            }

            db.SaveChanges();
        }

        public void HeldThroughSave(AppDbContext db)
        {
            using var transaction = db.Database.BeginTransaction();
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitlyHeldThroughSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
            transaction.Commit();
        }
        public void StableAliasWithoutTermination(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void DisposedAliasBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            alias.Dispose();
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task DisposedAsyncAliasBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await alias.DisposeAsync();
            await db.SaveChangesAsync();
        }

        public void CommittedAliasChainBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var secondAlias = alias;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            secondAlias.Commit();
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task CommittedAsyncAliasBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await alias.CommitAsync();
            await db.SaveChangesAsync();
        }

        public void RolledBackAliasBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            alias.Rollback();
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task RolledBackAsyncAliasBeforeSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await alias.RollbackAsync();
            await db.SaveChangesAsync();
        }

        public void ReassignedAliasInvalidatesProtection(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            alias = null;
            db.SaveChanges();
        }

        public void RefAliasInvalidatesProtection(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            Rebind(ref alias);
            db.SaveChanges();
        }

        public void AmbiguousAliasInvalidatesProtection(
            AppDbContext db,
            bool useTransaction)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = useTransaction ? transaction : null;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void NonNullUseTransaction(
            AppDbContext db,
            System.Data.Common.DbTransaction transaction)
        {
            db.Database.UseTransaction(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task NonNullUseTransactionAsync(
            AppDbContext db,
            System.Data.Common.DbTransaction transaction)
        {
            await db.Database.UseTransactionAsync(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public void NullUseTransaction(AppDbContext db)
        {
            db.Database.UseTransaction(null);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DefaultUseTransaction(AppDbContext db)
        {
            db.Database.UseTransaction(
                default(System.Data.Common.DbTransaction));
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ProvenNullUseTransaction(AppDbContext db)
        {
            System.Data.Common.DbTransaction transaction = null;
            db.Database.UseTransaction(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UnknownNullableUseTransaction(
            AppDbContext db,
            System.Data.Common.DbTransaction? transaction)
        {
            db.Database.UseTransaction(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GuardedUseTransaction(
            AppDbContext db,
            System.Data.Common.DbTransaction? transaction)
        {
            if (transaction is not null)
            {
                db.Database.UseTransaction(transaction);
                var order = db.Orders.First();
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }

        public void NullUseTransactionClearsPriorTransaction(AppDbContext db)
        {
            System.Data.Common.DbTransaction transaction = null;
            db.Database.UseTransaction(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Database.UseTransaction(null);
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task AwaitedBeginTransactionAsync(AppDbContext db)
        {
            await db.Database.BeginTransactionAsync();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task ConfiguredBeginTransactionAsync(AppDbContext db)
        {
            await db.Database.BeginTransactionAsync().ConfigureAwait(false);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public void UnobservedBeginTransactionAsync(AppDbContext db)
        {
            db.Database.BeginTransactionAsync();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DiscardedBeginTransactionAsync(AppDbContext db)
        {
            _ = db.Database.BeginTransactionAsync();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StoredBeginTransactionAsync(AppDbContext db)
        {
            var pending = db.Database.BeginTransactionAsync();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public async System.Threading.Tasks.Task WrappedBeginTransactionAsync(AppDbContext db)
        {
            await Wrap(db.Database.BeginTransactionAsync());
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task UnobservedUseTransactionAsync(
            AppDbContext db,
            System.Data.Common.DbTransaction transaction)
        {
            db.Database.UseTransactionAsync(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task ConfiguredUseTransactionAsync(
            AppDbContext db,
            System.Data.Common.DbTransaction transaction)
        {
            await db.Database.UseTransactionAsync(transaction).ConfigureAwait(false);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public void BlockingTaskResultHeldThroughSave(AppDbContext db)
        {
            using var transaction = db.Database.BeginTransactionAsync().Result;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void BlockingValueTaskResultHeldThroughSave(AppDbContext db)
        {
            using var transaction = db.Database.BeginTransactionAsync(true).Result;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BlockingTaskGetResultHeldThroughSave(AppDbContext db)
        {
            var transaction = db.Database.BeginTransactionAsync().GetAwaiter().GetResult();
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
            transaction.Commit();
        }

        public void BlockingConfiguredTaskGetResultHeldThroughSave(AppDbContext db)
        {
            var transaction = db.Database
                .BeginTransactionAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
            transaction.Commit();
        }

        public void BlockingConfiguredValueTaskGetResultHeldThroughSave(AppDbContext db)
        {
            var transaction = db.Database
                .BeginTransactionAsync(true)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
            transaction.Commit();
        }

        public void ResultLookalikeDoesNotProtect(AppDbContext db)
        {
            using var transaction = db.Database.BeginTransactionAsync(0).Result;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void AwaiterLookalikeDoesNotProtect(AppDbContext db)
        {
            using var transaction = db.Database
                .BeginTransactionAsync("")
                .GetAwaiter()
                .GetResult();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StoredTaskResultDoesNotProtect(AppDbContext db)
        {
            var pending = db.Database.BeginTransactionAsync();
            using var transaction = pending.Result;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BlockingResultTerminatedBeforeReadDoesNotProtect(AppDbContext db)
        {
            var transaction = db.Database.BeginTransactionAsync().Result;
            transaction.Dispose();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ChainedBlockingResultTerminationDoesNotProtect(AppDbContext db)
        {
            db.Database.BeginTransactionAsync().Result.Dispose();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BlockingResultScopeEndingBeforeSaveDoesNotProtect(AppDbContext db)
        {
            var order = db.Orders.First();
            using (var transaction = db.Database.BeginTransactionAsync().Result)
            {
                {|LC048:order.Quantity|}++;
            }

            db.SaveChanges();
        }

        public void OpaqueValueEscapeInvalidatesLifetime(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            var alias = transaction;
            Opaque(alias);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void PrivateSummaryPreservesLifetime(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            Inspect(transaction);
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void PrivateTerminationInvalidatesLifetime(AppDbContext db)
        {
            var transaction = db.Database.BeginTransaction();
            Terminate(transaction);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ChainedDisposeDoesNotProtect(AppDbContext db)
        {
            db.Database.BeginTransaction().Dispose();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void NestedDisposeDoesNotProtect(AppDbContext db)
        {
            DisposeNow(db.Database.BeginTransaction());
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DirectResultEscapeDoesNotProtect(AppDbContext db)
        {
            Opaque(db.Database.BeginTransaction());
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DirectUsingScopeProtectsThroughSave(AppDbContext db)
        {
            using (db.Database.BeginTransaction())
            {
                var order = db.Orders.First();
                order.Quantity++;
                db.SaveChanges();
            }
        }

        public async System.Threading.Tasks.Task ChainedAsyncCommitDoesNotProtect(AppDbContext db)
        {
            (await db.Database.BeginTransactionAsync()).Commit();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task ChainedAsyncDisposeDoesNotProtect(AppDbContext db)
        {
            await (await db.Database.BeginTransactionAsync().ConfigureAwait(false)).DisposeAsync();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task ChainedUseTransactionDoesNotProtect(
            AppDbContext db,
            System.Data.Common.DbTransaction transaction)
        {
            (await db.Database.UseTransactionAsync(transaction)).Rollback();
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public static void Opaque(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) { }

        private static void DisposeNow(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) =>
            transaction.Dispose();

        private static void Inspect(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            if (transaction == null)
                throw new System.InvalidOperationException();
        }

        private static void Terminate(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            transaction.Dispose();
        }

        private static System.Threading.Tasks.Task<
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> Wrap(
                System.Threading.Tasks.Task<
                    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> pending) => pending;

        private static void Rebind(
            ref Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            transaction = null;
        }

    }
}
"""
        );
    }

    [Fact]
    public async Task UnrelatedLinqAndLookalikeContextDoNotReport()
    {
        await VerifyAsync(
            """
namespace Test
{
    using System.Linq;
    public sealed class Item { public int Count { get; set; } }
    public sealed class Store
    {
        public Item[] Items { get; set; }
        public void SaveChanges() { }
    }
    public sealed class Service
    {
        public void Update(Store store)
        {
            var item = store.Items.First();
            item.Count++;
            store.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task KnownShapePreservingQueryOperatorsReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders
                .Where(x => x.Id > 0)
                .OrderBy(x => x.Id)
                .First();
            {|LC048:order.Quantity|}--;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task DbSetOriginsAndUnsupportedQueryShapesAreConservative()
    {
        await VerifyAsync(
            Domain
                + """
    public static class CustomQueries
    {
        public static IQueryable<Order> Passthrough(this IQueryable<Order> source) => source;
    }

    public sealed class GetOnlyContext : DbContext
    {
        public DbSet<Order> Orders { get; }
    }

    public sealed class MutableContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public class OverridableContext : DbContext
    {
        public virtual DbSet<Order> Orders { get; set; }
    }

    public sealed class ComputedSetContext : DbContext
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    public class BaseRoutingContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class HidingRoutingContext : BaseRoutingContext
    {
        public new DbSet<Order> Orders { get; set; }
    }

    public sealed class RefRoutingContext : DbContext
    {
        private DbSet<Order> _orders;
        public ref DbSet<Order> Orders => ref _orders;
    }

    public sealed class Service
    {
        public AppDbContext Db => new AppDbContext();

        public void Projected(AppDbContext db)
        {
            var order = db.Orders
                .Select(x => new Order { Id = x.Id, Quantity = x.Quantity })
                .First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Custom(AppDbContext db)
        {
            var order = db.Orders.Passthrough().First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ComputedContext()
        {
            var order = Db.Orders.First();
            order.Quantity++;
            Db.SaveChanges();
        }
        public void GetOnlyAutoPropertyReports(GetOnlyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StandardSetOriginReports(AppDbContext db)
        {
            var order = db.Set<Order>().First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ReassignedBeforeMaterializationDoesNotAttribute(
            MutableContext db,
            AppDbContext replacement)
        {
            db.Orders = replacement.Orders;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ConditionalWriteBeforeMaterializationDoesNotAttribute(
            MutableContext db,
            AppDbContext replacement,
            bool replace)
        {
            if (replace)
                db.Orders = replacement.Orders;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void WriteAfterMaterializationKeepsAttribution(
            MutableContext db,
            AppDbContext replacement)
        {
            var order = db.Orders.First();
            db.Orders = replacement.Orders;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void OverridableRootDoesNotAttribute(OverridableContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ComputedRootDoesNotAttribute(ComputedSetContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void HidingRootDoesNotAttribute(HidingRoutingContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void RefEscapeDoesNotAttribute(
            RefRoutingContext db,
            AppDbContext replacement)
        {
            Escape(ref db.Orders, replacement.Orders);
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        private static void Escape(
            ref DbSet<Order> target,
            DbSet<Order> replacement) => target = replacement;

    }
}
"""
        );
    }

    [Fact]
    public async Task LocalFunctionsRequireProvenInvocationAndPreserveEffects()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Uninvoked(AppDbContext db)
        {
            var order = db.Orders.First();
            System.Action mutate = () => order.Quantity++;
            void MutateLater() => order.Quantity++;
            db.SaveChanges();
        }

        public void CapturedMutationAndSave(AppDbContext db)
        {
            var order = db.Orders.First();
            void Persist()
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }

            Persist();
        }

        public async Task DirectParametersAndObservedAsync(AppDbContext db)
        {
            var order = db.Orders.First();
            async Task Persist(Order entity, AppDbContext context)
            {
                {|LC048:entity.Quantity|}++;
                await context.SaveChangesAsync();
            }

            await Persist(order, db);
        }

        public void MutationThenOuterSave(AppDbContext db)
        {
            var order = db.Orders.First();
            void Mutate(Order entity) => {|LC048:entity.Quantity|}++;
            Mutate(order);
            db.SaveChanges();
        }

        public void OuterMutationThenLocalSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            void Save(AppDbContext context) => context.SaveChanges();
            Save(db);
        }

        public void ConstantPredicates(AppDbContext db)
        {
            var first = db.Orders.First();
            var second = db.Orders.First();
            void Persist(Order entity, AppDbContext context, bool enabled)
            {
                if (enabled)
                {
                    {|LC048:entity.Quantity|}++;
                    context.SaveChanges();
                }
            }

            Persist(first, db, false);
            Persist(second, db, true);
        }

        public void OverwriteBeforeSave(AppDbContext db)
        {
            var order = db.Orders.First();
            void Persist(Order entity, AppDbContext context)
            {
                entity.Quantity++;
                entity.Quantity = 0;
                context.SaveChanges();
            }

            Persist(order, db);
        }

        public void StableTransactionAfterReadDoesNotCoverContainedSave(AppDbContext db)
        {
            var order = db.Orders.First();
            void Persist(Order entity, AppDbContext context)
            {
                using var transaction = context.Database.BeginTransaction();
                {|LC048:entity.Quantity|}++;
                context.SaveChanges();
            }

            Persist(order, db);
        }

        public void UnboundTransactionDoesNotCoverContainedSave(AppDbContext db)
        {
            var order = db.Orders.First();
            void Persist(Order entity, AppDbContext context)
            {
                context.Database.BeginTransaction();
                {|LC048:entity.Quantity|}++;
                context.SaveChanges();
            }

            Persist(order, db);
        }

        public void RecursiveEscapedAndCapturedPredicateStayConservative(
            AppDbContext db,
            bool enabled)
        {
            var recursiveOrder = db.Orders.First();
            void Recursive()
            {
                recursiveOrder.Quantity++;
                db.SaveChanges();
                Recursive();
            }
            Recursive();

            var escapedOrder = db.Orders.First();
            void Escaped()
            {
                escapedOrder.Quantity++;
                db.SaveChanges();
            }
            System.Action escaped = Escaped;
            Escaped();

            var conditionalOrder = db.Orders.First();
            void ConditionalCapture()
            {
                if (enabled)
                {
                    conditionalOrder.Quantity++;
                    db.SaveChanges();
                }
            }
            ConditionalCapture();
        }

        public void UnobservedAsyncAndCrossRootStayConservative(AppDbContext db)
        {
            var asyncOrder = db.Orders.First();
            async Task PersistLater()
            {
                await Task.Yield();
                asyncOrder.Quantity++;
                await db.SaveChangesAsync();
            }
            _ = PersistLater();

            var nestedOrder = db.Orders.First();
            void Outer()
            {
                void Inner()
                {
                    nestedOrder.Quantity++;
                    db.SaveChanges();
                }
                Inner();
            }
            Outer();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task SaveWithoutAReachableMutationPathDoesNotReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void GuardClause(AppDbContext db, bool stop)
        {
            var order = db.Orders.First();
            if (stop)
            {
                order.Quantity++;
                return;
            }

            db.SaveChanges();
        }

        public void OppositeBranches(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            if (mutate)
                order.Quantity++;
            else
                db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task HelperCfgPairsOnlyReachableEffectsAndPreservesEarlyBranchFallthrough()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        private readonly AppDbContext _db;

        public Service(AppDbContext db) => _db = db;

        public void ConditionalHelper(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            ApplyOrSave(order, db, mutate);
        }

        public void ReachableAfterEarlyReturn(AppDbContext db, bool stop)
        {
            var order = db.Orders.First();
            ApplyAfterGuard(order, db, false);
        }

        public void ConditionalSaveFalse(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            SaveConditionally(db, false);
        }

        public void ConditionalSaveTrue(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            SaveConditionally(db, true);
        }

        public void ConditionalSaveUnknown(AppDbContext db, bool save)
        {
            var order = db.Orders.First();
            order.Quantity++;
            SaveConditionally(db, save);
        }

        public void UnconditionalHelperSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            SaveAlways(db);
        }

        public void ConditionalMutationFalseCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenEnabledFalse(order, false);
            db.SaveChanges();
        }

        public void ConditionalMutationTrueCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenEnabledTrue(order, true);
            db.SaveChanges();
        }

        public void ConditionalMutationUnknownCallerSave(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            MutateWhenEnabledUnknown(order, mutate);
            db.SaveChanges();
        }

        public void NegatedMutationDisabledCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenNotDisabled(order, true);
            db.SaveChanges();
        }

        public void NegatedMutationEnabledCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenNotEnabled(order, false);
            db.SaveChanges();
        }

        public void ExactBooleanMutationFalseCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenExactlyTrueFalse(order, false);
            db.SaveChanges();
        }

        public void ExactBooleanMutationTrueCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenExactlyTrueTrue(order, true);
            db.SaveChanges();
        }

        public void CompoundMutationGuardRemainsConservative(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenCompound(order, false, true);
            db.SaveChanges();
        }

        public void ConditionalMutationGuardRemainsConservative(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateWhenConditional(order, false, true);
            db.SaveChanges();
        }

        public void ConditionalMutationFalseHelperSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateConditionallyAndSaveFalse(order, db, false);
        }

        public void ConditionalMutationTrueHelperSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateConditionallyAndSaveTrue(order, db, true);
        }

        public void NegatedConditionalSaveFalse(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            SaveWhenNot(db, true);
        }

        public void NegatedConditionalSaveTrue(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            SaveWhenNot(db, false);
        }

        public void ExactBooleanConditionalSaveFalse(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            SaveWhenExactlyTrue(db, false);
        }

        public void ExactBooleanConditionalSaveTrue(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            SaveWhenExactlyTrue(db, true);
        }

        public void TerminalEffects(AppDbContext db)
        {
            var order = db.Orders.First();
            DeadAfterReturn(order, db);
            DeadSaveAfterReturn(order, db);
            DeadAfterThrow(order, db);
        }

        public void OtherInstance(Service other)
        {
            var order = _db.Orders.First();
            order.Quantity++;
            other._db.SaveChanges();
        }

        private static void MutateWhenEnabledFalse(Order order, bool mutate)
        {
            if (mutate)
                order.Quantity++;
        }

        private static void MutateWhenEnabledTrue(Order order, bool mutate)
        {
            if (mutate)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateWhenEnabledUnknown(Order order, bool mutate)
        {
            if (mutate)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateWhenNotDisabled(Order order, bool mutate)
        {
            if (!mutate)
                order.Quantity++;
        }

        private static void MutateWhenNotEnabled(Order order, bool mutate)
        {
            if (!mutate)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateWhenExactlyTrueFalse(Order order, bool mutate)
        {
            if (mutate == true)
                order.Quantity++;
        }

        private static void MutateWhenExactlyTrueTrue(Order order, bool mutate)
        {
            if (mutate == true)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateWhenCompound(Order order, bool mutate, bool other)
        {
            if (mutate && other)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateWhenConditional(Order order, bool mutate, bool other)
        {
            if (mutate ? other : false)
                {|LC048:order.Quantity|}++;
        }

        private static void MutateConditionallyAndSaveFalse(
            Order order,
            AppDbContext db,
            bool mutate)
        {
            if (mutate)
                order.Quantity++;
            db.SaveChanges();
        }

        private static void MutateConditionallyAndSaveTrue(
            Order order,
            AppDbContext db,
            bool mutate)
        {
            if (mutate)
                {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        private static void SaveWhenNot(AppDbContext db, bool save)
        {
            if (!save)
                db.SaveChanges();
        }

        private static void SaveWhenExactlyTrue(AppDbContext db, bool save)
        {
            if (save == true)
                db.SaveChanges();
        }

        private static void ApplyOrSave(Order order, AppDbContext db, bool mutate)
        {
            if (mutate)
                order.Quantity++;
            else
                db.SaveChanges();
        }

        private static void ApplyAfterGuard(
            Order order,
            AppDbContext db,
            bool stop)
        {
            if (stop)
                return;

            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        private static void SaveConditionally(AppDbContext db, bool save)
        {
            if (save)
                db.SaveChanges();
        }

        private static void SaveAlways(AppDbContext db)
        {
            db.SaveChanges();
        }

        private static void DeadAfterReturn(Order order, AppDbContext db)
        {
            return;
            order.Quantity++;
            db.SaveChanges();
        }

        private static void DeadSaveAfterReturn(Order order, AppDbContext db)
        {
            order.Quantity++;
            return;
            db.SaveChanges();
        }

        private static void DeadAfterThrow(Order order, AppDbContext db)
        {
            order.Quantity++;
            throw new InvalidOperationException();
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CurrentInstanceReadonlyContextFieldReports()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        private readonly AppDbContext _db;

        public Service(AppDbContext db) => _db = db;

        public void Update()
        {
            var order = _db.Orders.First();
            {|LC048:order.Quantity|}++;
            _db.SaveChanges();
        }

        public void UpdateAfterUnrelatedHelper()
        {
            var order = _db.Orders.First();
            Observe();
            {|LC048:order.Quantity|}++;
            _db.SaveChanges();
        }

        private static void Observe() => Console.WriteLine("observed");
    }

    public sealed class MutableFieldService
    {
        private AppDbContext _db;

        public MutableFieldService(AppDbContext db) => _db = db;

        public void NestedHelperRebindStaysQuiet(AppDbContext replacement)
        {
            var order = _db.Orders.First();
            Rebind(replacement);
            order.Quantity++;
            _db.SaveChanges();
        }

        public void RefEscapeStaysQuiet(AppDbContext replacement)
        {
            var order = _db.Orders.First();
            Escape(ref _db, replacement);
            order.Quantity++;
            _db.SaveChanges();
        }

        public void UnrelatedHelperReports()
        {
            var order = _db.Orders.First();
            Observe();
            {|LC048:order.Quantity|}++;
            _db.SaveChanges();
        }

        private void Rebind(AppDbContext replacement) => ApplyReplacement(replacement);
        private void ApplyReplacement(AppDbContext replacement) => _db = replacement;
        private static void Escape(
            ref AppDbContext context,
            AppDbContext replacement) => context = replacement;
        private static void Observe() => Console.WriteLine("observed");
    }

    public abstract class OpaqueFieldService
    {
        private AppDbContext _db;

        protected OpaqueFieldService(AppDbContext db) => _db = db;

        public void OpaqueCallStaysQuiet()
        {
            var order = _db.Orders.First();
            MayRebind();
            order.Quantity++;
            _db.SaveChanges();
        }

        protected abstract void MayRebind();
    }

    public sealed class StaticReadonlyFieldService
    {
        private static readonly AppDbContext Db = new();

        public void Update()
        {
            var order = Db.Orders.First();
            {|LC048:order.Quantity|}++;
            Db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task DbContextFindAndExplicitReattachmentReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class LookalikeFindContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new ValueTask<TEntity> FindAsync<TEntity>(params object[] keys)
            where TEntity : class => new ValueTask<TEntity>(default(TEntity));
    }

    public sealed class LookalikeDbSet<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public new ValueTask<TEntity> FindAsync(params object[] keys) =>
            new ValueTask<TEntity>(default(TEntity));
    }

    public sealed class LookalikeSetContext : DbContext
    {
        public LookalikeDbSet<Order> Orders { get; set; }
    }

    public sealed class DelegatingFindContext : DbContext
    {
        public override TEntity Find<TEntity>(params object[] keys) =>
            base.Find<TEntity>(keys);
        public override ValueTask<TEntity> FindAsync<TEntity>(params object[] keys) =>
            base.FindAsync<TEntity>(keys);
        public override ValueTask<TEntity> FindAsync<TEntity>(
            object[] keys,
            CancellationToken cancellationToken) =>
            base.FindAsync<TEntity>(keys, cancellationToken);
    }

    public sealed class DelegatingFindDbSet<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public override TEntity Find(params object[] keys) => base.Find(keys);
        public override ValueTask<TEntity> FindAsync(params object[] keys) =>
            base.FindAsync(keys);
        public override ValueTask<TEntity> FindAsync(
            object[] keys,
            CancellationToken cancellationToken) =>
            base.FindAsync(keys, cancellationToken);
    }

    public sealed class DelegatingFindSetContext : DbContext
    {
        public DelegatingFindDbSet<Order> Orders { get; set; }
    }

    public class HidingFindBaseContext : DbContext
    {
        public new virtual TEntity Find<TEntity>(params object[] keys)
            where TEntity : class => default;
        public new virtual ValueTask<TEntity> FindAsync<TEntity>(params object[] keys)
            where TEntity : class => new ValueTask<TEntity>(default(TEntity));
    }

    public sealed class InvalidFindOverrideContext : HidingFindBaseContext
    {
        public override TEntity Find<TEntity>(params object[] keys) =>
            base.Find<TEntity>(keys);
        public override ValueTask<TEntity> FindAsync<TEntity>(params object[] keys) =>
            base.FindAsync<TEntity>(keys);
    }

    public class HidingFindDbSetBase<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public new virtual TEntity Find(params object[] keys) => default;
        public new virtual ValueTask<TEntity> FindAsync(params object[] keys) =>
            new ValueTask<TEntity>(default(TEntity));
    }

    public sealed class InvalidFindOverrideDbSet<TEntity> : HidingFindDbSetBase<TEntity>
        where TEntity : class
    {
        public override TEntity Find(params object[] keys) => base.Find(keys);
        public override ValueTask<TEntity> FindAsync(params object[] keys) =>
            base.FindAsync(keys);
    }

    public sealed class InvalidFindOverrideSetContext : DbContext
    {
        public InvalidFindOverrideDbSet<Order> Orders { get; set; }
    }

    public class CustomFindOverloadContext : DbContext
    {
        public virtual Order Find(int key) => default;
    }

    public sealed class CustomFindOverloadOverrideContext : CustomFindOverloadContext
    {
        public override Order Find(int key) => base.Find(key);
    }

    public sealed class Service
    {
        public async Task Update(AppDbContext db)
        {
            var direct = db.Find<Order>(1);
            {|LC048:direct.Quantity|}++;
            db.SaveChanges();

            var asynchronous = await db.FindAsync<Order>(2);
            {|LC048:asynchronous.Quantity|} += 2;
            await db.SaveChangesAsync();

            var reattached = db.Orders.AsNoTracking().First();
            db.Update(reattached);
            {|LC048:reattached.Quantity|}--;
            db.SaveChanges();
        }

        public async Task DelegatedOverridesReport(
            DelegatingFindContext context,
            DelegatingFindSetContext setContext)
        {
            var contextSync = context.Find<Order>(1);
            {|LC048:contextSync.Quantity|}++;
            context.SaveChanges();

            var contextAsync = await context.FindAsync<Order>(
                new object[] { 2 },
                default);
            {|LC048:contextAsync.Quantity|}++;
            context.SaveChanges();

            var setSync = setContext.Orders.Find(3);
            {|LC048:setSync.Quantity|}++;
            setContext.SaveChanges();

            var setAsync = await setContext.Orders.FindAsync(4);
            {|LC048:setAsync.Quantity|}++;
            setContext.SaveChanges();
        }

        public async Task InvalidOverrideChainsAreNotTerminals(
            InvalidFindOverrideContext context,
            InvalidFindOverrideSetContext setContext,
            CustomFindOverloadOverrideContext overloadContext)
        {
            var contextSync = context.Find<Order>(1);
            contextSync.Quantity++;
            context.SaveChanges();

            var contextAsync = await context.FindAsync<Order>(2);
            contextAsync.Quantity++;
            context.SaveChanges();

            var setSync = setContext.Orders.Find(3);
            setSync.Quantity++;
            setContext.SaveChanges();

            var setAsync = await setContext.Orders.FindAsync(4);
            setAsync.Quantity++;
            setContext.SaveChanges();

            var overload = overloadContext.Find(5);
            overload.Quantity++;
            overloadContext.SaveChanges();
        }

        public async Task CustomFindDeclarationsAreNotTerminals(
            LookalikeFindContext context,
            LookalikeSetContext setContext)
        {
            var contextEntity = await context.FindAsync<Order>(1);
            contextEntity.Quantity++;
            context.SaveChanges();

            var setEntity = await setContext.Orders.FindAsync(1);
            setEntity.Quantity++;
            setContext.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task FluentConfigurationRequiresActualBuilderAndClrIndexersAreExcluded()
    {
        await VerifyAsync(
            Domain
                + """
    public class BaseOrder
    {
        public int Version { get; set; }
    }

    public sealed class DerivedOrder : BaseOrder
    {
        public int Quantity { get; set; }
    }

    public sealed class ProtectedDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class DisabledDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property(x => x.Status)
                .IsConcurrencyToken()
                .IsConcurrencyToken(false);
        }
    }

    public sealed class DerivedDbContext : DbContext
    {
        public DbSet<DerivedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BaseOrder>().Property(x => x.Version).IsRowVersion();
        }
    }

    public sealed class CallbackProtectedDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(builder =>
                builder.Property(x => x.Quantity).IsConcurrencyToken());
        }
    }

    public sealed class ConditionalDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.Now.Ticks > 0)
                modelBuilder.Entity<Order>().Property(x => x.Status).IsConcurrencyToken();
        }
    }

    public sealed class MultiPropertyDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
            modelBuilder.Entity<Order>().Property(x => x.Name).IsConcurrencyToken(false);
        }
    }

    public sealed class OtherBuilderContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            GetOtherBuilder().Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }

        private static ModelBuilder GetOtherBuilder() => new ModelBuilder();
    }

    public sealed class AliasedBuilderContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var stableBuilder = modelBuilder;
            var entityBuilder = stableBuilder.Entity<Order>();
            var propertyBuilder = entityBuilder.Property(x => x.Status);
            propertyBuilder.IsConcurrencyToken();
        }
    }

    public sealed class AliasedKeyContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entityBuilder = modelBuilder.Entity<Order>();
            entityBuilder.HasKey(x => x.Status);
        }
    }

    public sealed class AliasedIgnoreContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entityBuilder = modelBuilder.Entity<Order>();
            entityBuilder.Ignore(x => x.Name);
        }
    }

    public sealed class AliasedMappedOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Quantity { get; set; }
    }

    public sealed class AliasedMappingContext : DbContext
    {
        public DbSet<AliasedMappedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entityBuilder = modelBuilder.Entity<AliasedMappedOrder>();
            entityBuilder.Property(x => x.Quantity);
        }
    }

    public sealed class RejectedBuilderAliasContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var reassigned = modelBuilder.Entity<Order>();
            reassigned = new Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order>();
            reassigned.Ignore(x => x.Quantity);

            var reassignedProperty = modelBuilder.Entity<Order>().Property(x => x.Id);
            reassignedProperty =
                new Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<int>();
            reassignedProperty.IsConcurrencyToken();

            var conditional = DateTime.UtcNow.Ticks > 0
                ? modelBuilder.Entity<Order>()
                : new Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order>();
            conditional.Property(x => x.Status).IsConcurrencyToken();

            var foreign = new Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order>();
            foreign.HasKey(x => x.Name);

            modelBuilder.Entity<Order>(builder =>
            {
                var foreignCallback =
                    new Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order>();
                foreignCallback.Property(x => x.Id).IsConcurrencyToken();
            });
        }
    }

    public sealed class ComputedOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public int Status { get; set; }
        public int Name { get; set; }
    }

    public sealed class DirectGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }
        public DbSet<Order> OtherOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<ComputedOrder>()
                .Property(x => x.Quantity)
                .HasComputedColumnSql("Quantity + 1");
            modelBuilder
                .Entity<ComputedOrder>()
                .Property(x => x.Status)
                .ValueGeneratedOnAddOrUpdate();
        }
    }

    public sealed class PlainGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }
    }

    public sealed class AliasedGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<ComputedOrder>();
            var property = entity.Property(x => x.Name);
            property.HasComputedColumnSql("1");
        }
    }

    public class BaseGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<ComputedOrder>()
                .Property(x => x.Quantity)
                .ValueGeneratedOnAddOrUpdate();
        }
    }

    public sealed class DerivedGeneratedOverrideContext : BaseGeneratedContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ComputedOrder>()
                .Property(x => x.Quantity)
                .ValueGeneratedNever();
        }
    }

    public sealed class DerivedGeneratedBaseLastContext : BaseGeneratedContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<ComputedOrder>()
                .Property(x => x.Quantity)
                .ValueGeneratedNever();
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class RemappedGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<ComputedOrder>();
            entity.Property(x => x.Quantity).HasComputedColumnSql("1");
            entity.Ignore(x => x.Quantity);
            entity.Property(x => x.Quantity);
        }
    }

    public sealed class ConditionalGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.UtcNow.Ticks > 0)
            {
                modelBuilder
                    .Entity<ComputedOrder>()
                    .Property(x => x.Quantity)
                    .HasComputedColumnSql("1");
            }
        }
    }

    public sealed class ConditionalGeneratedOverrideContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<ComputedOrder>().Property(x => x.Quantity);
            property.ValueGeneratedOnAddOrUpdate();
            if (DateTime.UtcNow.Ticks > 0)
                property.ValueGeneratedNever();
        }
    }

    public sealed class LookalikeGeneratedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            LookalikeComputedExtensions.HasComputedColumnSql(
                modelBuilder.Entity<ComputedOrder>().Property(x => x.Quantity),
                "1");
        }
    }

    public static class LookalikeComputedExtensions
    {
        public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T>
            HasComputedColumnSql<T>(
                Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> builder,
                string sql) => builder;
    }

    public sealed class DisabledRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property(x => x.Status)
                .IsRowVersion()
                .ValueGeneratedNever();
        }
    }

    public sealed class RestoredGeneratedRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property(x => x.Status)
                .IsRowVersion()
                .ValueGeneratedNever()
                .ValueGeneratedOnAddOrUpdate();
        }
    }

    public sealed class RestoredOnUpdateRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            property.ValueGeneratedOnUpdate();
        }
    }

    public sealed class DisabledAfterOnUpdateRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property(x => x.Status)
                .IsRowVersion()
                .ValueGeneratedOnUpdate()
                .ValueGeneratedNever();
        }
    }

    public sealed class ConditionalOnUpdateRestoreRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            if (DateTime.UtcNow.Ticks > 0)
                property.ValueGeneratedOnUpdate();
        }
    }

    public sealed class GuaranteedOnUpdateRestoreRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            if (true)
                property.ValueGeneratedOnUpdate();
        }
    }

    public sealed class RestoredRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            property.IsRowVersion();
        }
    }

    public sealed class AliasedDisabledRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<Order>();
            var property = entity.Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
        }
    }

    public sealed class ConditionalDisabledRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            if (DateTime.UtcNow.Ticks > 0)
                property.ValueGeneratedNever();
        }
    }

    public sealed class GuaranteedRestoredRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            if (true)
                property.ValueGeneratedOnAddOrUpdate();
        }
    }

    public sealed class ScopedPlainRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class OtherRowVersionEntity
    {
        public int Id { get; set; }
        public int Status { get; set; }
    }

    public sealed class OtherEntityRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OtherRowVersionEntity> OtherOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<OtherRowVersionEntity>()
                .Property(x => x.Status)
                .IsRowVersion();
        }
    }

    public sealed class OtherPropertyGeneratedOverrideContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Status).IsRowVersion();
            modelBuilder.Entity<Order>().Property(x => x.Quantity).ValueGeneratedNever();
        }
    }

    public sealed class RemappedDisabledRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<Order>();
            entity.Property(x => x.Status).IsRowVersion();
            entity.Ignore(x => x.Status);
            entity.Property(x => x.Status);
        }
    }

    public class BaseRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Status).IsRowVersion();
        }
    }

    public sealed class DerivedDisabledRowVersionContext : BaseRowVersionContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(x => x.Status).ValueGeneratedNever();
        }
    }

    public sealed class DerivedBaseLastRowVersionContext : BaseRowVersionContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Status).ValueGeneratedNever();
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class NamedDisabledRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property<byte[]>("ShadowVersion");
            property.IsRowVersion();
            property.ValueGeneratedNever();
        }
    }

    public sealed class NamedRestoredRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property<byte[]>("ShadowVersion");
            property.IsRowVersion();
            property.ValueGeneratedNever();
            property.ValueGeneratedOnAddOrUpdate();
        }
    }

    public sealed class NamedRemappedRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<Order>();
            entity.Property<byte[]>("ShadowVersion").IsRowVersion();
            entity.Ignore("ShadowVersion");
            entity.Property<byte[]>("ShadowVersion");
        }
    }

    public sealed class LookalikeDisableRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            LookalikeGenerationExtensions.ValueGeneratedNever(property);
        }
    }

    public sealed class LookalikeRestoreRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            LookalikeGenerationExtensions.ValueGeneratedOnAddOrUpdate(property);
        }
    }

    public sealed class LookalikeOnUpdateRestoreRowVersionContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<Order>().Property(x => x.Status);
            property.IsRowVersion();
            property.ValueGeneratedNever();
            LookalikeGenerationExtensions.ValueGeneratedOnUpdate(property);
        }
    }

    public static class LookalikeGenerationExtensions
    {
        public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T>
            ValueGeneratedNever<T>(
                Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property) =>
                property;

        public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T>
            ValueGeneratedOnAddOrUpdate<T>(
                Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property) =>
                property;

        public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T>
            ValueGeneratedOnUpdate<T>(
                Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property) =>
                property;
    }

    public sealed class IndexedOrder
    {
        private readonly int[] values = new int[1];

        public int Id { get; set; }
        public int Quantity { get; set; }
        public int this[int index]
        {
            get => values[index];
            set => values[index] = value;
        }
    }

    public sealed class IndexedContext : DbContext
    {
        public DbSet<IndexedOrder> Orders { get; set; }
    }

    public sealed class Service
    {
        public void Protected(ProtectedDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Disabled(DisabledDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void CallbackProtected(CallbackProtectedDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Conditional(ConditionalDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void MultiProperty(MultiPropertyDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Inherited(DerivedDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
        public void OtherBuilderDoesNotConfigure(OtherBuilderContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StableAliasConfigures(AliasedBuilderContext db)
        {
            var order = db.Orders.First();
            order.Status++;
            db.SaveChanges();
        }

        public void StableKeyAliasConfigures(AliasedKeyContext db)
        {
            var order = db.Orders.First();
            order.Status++;
            db.SaveChanges();
        }

        public void StableIgnoreAliasConfigures(AliasedIgnoreContext db)
        {
            var order = db.Orders.First();
            order.Name += "!";
            db.SaveChanges();
        }

        public void StableMappingAliasConfigures(AliasedMappingContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UnstableAndForeignAliasesDoNotConfigure(RejectedBuilderAliasContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            {|LC048:order.Status|}++;
            {|LC048:order.Name|} += "!";
            db.SaveChanges();
        }

        public void IndexerIsNotScalarEvidence(IndexedContext db)
        {
            var order = db.Orders.First();
            order[0]++;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EffectiveComputedConfiguration(DirectGeneratedContext db)
        {
            var computed = db.Orders.First();
            computed.Quantity++;
            computed.Status++;
            {|LC048:computed.Name|}++;
            var otherEntity = db.OtherOrders.First();
            {|LC048:otherEntity.Quantity|}++;
            db.SaveChanges();
        }

        public void ComputedConfigurationIsContextScoped(PlainGeneratedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StableComputedAliasConfigures(AliasedGeneratedContext db)
        {
            var order = db.Orders.First();
            order.Name++;
            db.SaveChanges();
        }

        public void DerivedOverrideWins(DerivedGeneratedOverrideContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LaterBaseCallWins(DerivedGeneratedBaseLastContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void IgnoreThenRemapClearsGeneratedState(RemappedGeneratedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalGeneratedStateIsNotDefinite(ConditionalGeneratedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalWritableOverrideIsConservative(
            ConditionalGeneratedOverrideContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ComputedLookalikeDoesNotConfigure(LookalikeGeneratedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DisabledRowVersionDoesNotProtect(DisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LaterRealGenerationRestoresProtection(
            RestoredGeneratedRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ValueGeneratedOnUpdateRestoresProtection(
            RestoredOnUpdateRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void LaterNeverDisablesValueGeneratedOnUpdate(
            DisabledAfterOnUpdateRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalValueGeneratedOnUpdateIsNotDefinite(
            ConditionalOnUpdateRestoreRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GuaranteedValueGeneratedOnUpdateRestoresProtection(
            GuaranteedOnUpdateRestoreRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void LaterRealRowVersionRestoresProtection(RestoredRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void AliasedGenerationOverrideIsEffective(AliasedDisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalGenerationOverrideIsConservative(
            ConditionalDisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GuaranteedGenerationRestoreIsEffective(
            GuaranteedRestoredRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void RowVersionConfigurationIsContextScoped(ScopedPlainRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void RowVersionConfigurationIsEntityScoped(OtherEntityRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GenerationOverrideIsPropertyScoped(
            OtherPropertyGeneratedOverrideContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void IgnoreAndRemapDoNotRestoreGeneration(
            RemappedDisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DerivedOverrideAfterBaseCallWins(DerivedDisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LaterBaseCallRestoresGeneration(DerivedBaseLastRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NamedRowVersionGenerationOverrideIsEffective(
            NamedDisabledRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void NamedRowVersionGenerationRestoreIsEffective(
            NamedRestoredRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NamedIgnoreAndRemapDoNotRestoreGeneration(
            NamedRemappedRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeDisableDoesNotOverrideRealGeneration(
            LookalikeDisableRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void LookalikeRestoreDoesNotRestoreRealGeneration(
            LookalikeRestoreRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeValueGeneratedOnUpdateDoesNotRestoreGeneration(
            LookalikeOnUpdateRestoreRowVersionContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

    }
}
"""
        );
    }

    [Fact]
    public async Task HelperEffectOrderRefAliasesAndNestedSavesStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void HelperBeforeMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            SaveThenMutate(order, db);
        }

        public void RefAlias(AppDbContext db1, AppDbContext db2)
        {
            var alias = db1;
            Replace(ref alias, db2);
            var order = alias.Orders.First();
            order.Quantity++;
            db1.SaveChanges();
        }

        public void NestedSave(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity += db.SaveChanges();
        }

        private static void SaveThenMutate(Order order, AppDbContext db)
        {
            db.SaveChanges();
            order.Quantity++;
        }

        private static void Replace(ref AppDbContext target, AppDbContext replacement)
        {
            target = replacement;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task NotMappedMutationAndMutuallyExclusiveAttachStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class TransientOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int TransientCount { get; set; }
    }

    public sealed class TransientDbContext : DbContext
    {
        public DbSet<TransientOrder> Orders { get; set; }
    }

    public sealed class Service
    {
        public void Transient(TransientDbContext db)
        {
            var order = db.Orders.First();
            order.TransientCount++;
            db.SaveChanges();
        }

        public void ConditionalAttach(AppDbContext db, bool attach)
        {
            var order = db.Orders.AsNoTracking().First();
            if (attach)
                db.Attach(order);
            else
                order.Quantity++;

            db.SaveChanges();
        }
        public void PostMutationAttach(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Attach(order);
            db.SaveChanges();
        }

        public void EntryState(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).State = EntityState.Modified;
            db.SaveChanges();
        }

    }
}
"""
        );
    }

    [Fact]
    public async Task LazyHelpersStayQuietAndUnboundHelperTransactionReports()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Lazy(AppDbContext db)
        {
            var order = db.Orders.First();
            _ = MutateAsync(order);
            var pending = MutateIterator(order);
            db.SaveChanges();
        }

        public void Transactional(AppDbContext db)
        {
            Begin(db);
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        private static async Task MutateAsync(Order order)
        {
            await Task.Yield();
            order.Quantity++;
        }

        private static IEnumerable<int> MutateIterator(Order order)
        {
            order.Quantity++;
            yield return order.Quantity;
        }

        private static void Begin(AppDbContext db) => db.Database.BeginTransaction();
    }
}
"""
        );
    }

    [Fact]
    public async Task StraightLineHelperAndSwitchLoopGuardsReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Helper(AppDbContext db)
        {
            var order = db.Orders.First();
            ApplyAndSave(order, db);
        }

        public void ConditionalOverwriteBeforeHelperSave(AppDbContext db, bool overwrite)
        {
            var order = db.Orders.First();
            MutateMaybeOverwriteAndSave(order, db, overwrite);
        }

        public void ConditionalOverwriteBeforeCallerSave(AppDbContext db, bool overwrite)
        {
            var order = db.Orders.First();
            MutateMaybeOverwrite(order, overwrite);
            db.SaveChanges();
        }

        public void DefiniteOverwriteBeforeHelperSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateOverwriteAndSave(order, db);
        }

        public void DefiniteOverwriteBeforeCallerSave(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateAndOverwrite(order);
            db.SaveChanges();
        }

        public void Switch(AppDbContext db)
        {
            var order = db.Orders.First();
            switch (order.Status)
            {
                case 0:
                    {|LC048:order.Status|} = 1;
                    break;
            }

            db.SaveChanges();
        }

        public void Loop(AppDbContext db)
        {
            var order = db.Orders.First();
            while (order.Status == 0)
            {
                {|LC048:order.Status|} = 1;
                break;
            }

            db.SaveChanges();
        }

        private static void ApplyAndSave(Order order, AppDbContext db)
        {
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        private static void MutateMaybeOverwriteAndSave(
            Order order,
            AppDbContext db,
            bool overwrite)
        {
            {|LC048:order.Quantity|}++;
            if (overwrite)
                order.Quantity = 42;
            db.SaveChanges();
        }

        private static void MutateMaybeOverwrite(Order order, bool overwrite)
        {
            {|LC048:order.Quantity|}++;
            if (overwrite)
                order.Quantity = 42;
        }

        private static void MutateOverwriteAndSave(Order order, AppDbContext db)
        {
            order.Quantity++;
            order.Quantity = 42;
            db.SaveChanges();
        }

        private static void MutateAndOverwrite(Order order)
        {
            order.Quantity++;
            order.Quantity = 42;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CoalescingAssignmentReportsWhileDefiniteOverwriteAndPostTestGuardStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Coalesce(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Name|} ??= "default";
            db.SaveChanges();
        }

        public void Overwritten(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            order.Quantity = 42;
            db.SaveChanges();
        }

        public void PostTest(AppDbContext db)
        {
            var order = db.Orders.First();
            do
            {
                order.Status = 2;
            } while (order.Status == 1);

            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task NonReturningHelperAndContextSpecificFluentProtectionStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class SharedProtectedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class SharedUnprotectedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class Service
    {
        public void NonReturning(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateThenThrow(order);
            db.SaveChanges();
        }

        public void Unprotected(SharedUnprotectedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Protected(SharedProtectedContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ReboundParameter(AppDbContext db)
        {
            var order = db.Orders.First();
            Rebind(order);
            db.SaveChanges();
        }

        private static void Rebind(Order order)
        {
            order = new Order();
            order.Quantity++;
        }

        private static void MutateThenThrow(Order order)
        {
            order.Quantity++;
            throw new InvalidOperationException();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ReassignedContextAndExplicitDetachmentStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        private AppDbContext _db;

        public Service(AppDbContext db)
        {
            _db = db;
        }

        public void Reassigned(AppDbContext db, AppDbContext replacement)
        {
            var order = db.Orders.First();
            db = replacement;
            order.Quantity++;
            db.SaveChanges();
        }

        public void ReassignedBeforeRead(AppDbContext db, AppDbContext replacement)
        {
            db = replacement;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ParameterWriteAfterSaveReports(
            AppDbContext db,
            AppDbContext replacement)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
            db = replacement;
        }

        public void MutuallyExclusiveParameterWriteReports(
            AppDbContext db,
            AppDbContext replacement,
            bool replace)
        {
            if (replace)
                db = replacement;
            if (!replace)
            {
                var order = db.Orders.First();
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }

        public void LocalWriteAfterSaveReports(
            AppDbContext db,
            AppDbContext replacement)
        {
            var context = db;
            var order = context.Orders.First();
            {|LC048:order.Quantity|}++;
            context.SaveChanges();
            context = replacement;
        }

        public void LocalWriteBeforeReadStaysQuiet(
            AppDbContext db,
            AppDbContext replacement)
        {
            var context = db;
            context = replacement;
            var order = context.Orders.First();
            {|LC048:order.Quantity|}++;
            context.SaveChanges();
        }

        public void FieldWriteAfterSaveReports(AppDbContext replacement)
        {
            var order = _db.Orders.First();
            {|LC048:order.Quantity|}++;
            _db.SaveChanges();
            _db = replacement;
        }

        public void FieldWriteBetweenReadAndSaveStaysQuiet(AppDbContext replacement)
        {
            var order = _db.Orders.First();
            _db = replacement;
            order.Quantity++;
            _db.SaveChanges();
        }

        public void ParameterAliasesAcrossWrite(
            AppDbContext db,
            AppDbContext replacement)
        {
            var readContext = db;
            db = replacement;
            var writeContext = db;
            var order = readContext.Orders.First();
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void ParameterAliasesAcrossWriteAfterRead(
            AppDbContext db,
            AppDbContext replacement)
        {
            var readContext = db;
            var order = readContext.Orders.First();
            db = replacement;
            var writeContext = db;
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void LocalRootAliasesAcrossWrite(
            AppDbContext db,
            AppDbContext replacement)
        {
            var root = db;
            var readContext = root;
            root = replacement;
            var writeContext = root;
            var order = readContext.Orders.First();
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void FieldAliasesAcrossWrite(AppDbContext replacement)
        {
            var readContext = _db;
            _db = replacement;
            var writeContext = _db;
            var order = readContext.Orders.First();
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void FieldAliasesAcrossHelperWrite(AppDbContext replacement)
        {
            var readContext = _db;
            ReplaceField(replacement);
            var writeContext = _db;
            var order = readContext.Orders.First();
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void ParameterAliasesAcrossRefWrite(
            AppDbContext db,
            AppDbContext replacement)
        {
            var readContext = db;
            Replace(ref db, replacement);
            var writeContext = db;
            var order = readContext.Orders.First();
            order.Quantity++;
            writeContext.SaveChanges();
        }

        public void BranchAliasesAcrossWrite(
            AppDbContext db,
            AppDbContext replacement,
            bool replace)
        {
            var readContext = db;
            if (replace)
            {
                db = replacement;
                var writeContext = db;
                var order = readContext.Orders.First();
                order.Quantity++;
                writeContext.SaveChanges();
            }
        }

        public void AliasesCapturedAfterWriteReport(
            AppDbContext db,
            AppDbContext replacement)
        {
            db = replacement;
            var readContext = db;
            var writeContext = db;
            var order = readContext.Orders.First();
            {|LC048:order.Quantity|}++;
            writeContext.SaveChanges();
        }

        public void RootWriteAfterAliasSaveReports(
            AppDbContext db,
            AppDbContext replacement)
        {
            var readContext = db;
            var writeContext = db;
            var order = readContext.Orders.First();
            {|LC048:order.Quantity|}++;
            writeContext.SaveChanges();
            db = replacement;
        }

        private void ReplaceField(AppDbContext replacement)
        {
            _db = replacement;
        }

        private static void Replace(ref AppDbContext target, AppDbContext replacement)
        {
            target = replacement;
        }

        public void Detached(AppDbContext db)
        {
            var order = db.Orders.First();
            db.Entry(order).State = EntityState.Detached;
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task EffectiveFluentModelHonorsBaseCallsAndExplicitFalseOverrides()
    {
        await VerifyAsync(
            Domain
                + """
    public class BaseProtectedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class DerivedWithoutBaseContext : BaseProtectedContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public sealed class DerivedWithBaseContext : BaseProtectedContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class DerivedDisablingContext : BaseProtectedContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<Order>()
                .Property(x => x.Quantity)
                .IsConcurrencyToken(false);
        }
    }

    public sealed class AttributedOrder
    {
        public int Id { get; set; }
        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int Quantity { get; set; }
        [System.ComponentModel.DataAnnotations.Timestamp]
        public byte[] Version { get; set; }
    }

    public sealed class AttributeOverrideContext : DbContext
    {
        public DbSet<AttributedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<AttributedOrder>()
                .Property(x => x.Quantity)
                .IsConcurrencyToken(false);
            modelBuilder
                .Entity<AttributedOrder>()
                .Property(x => x.Version)
                .IsConcurrencyToken(false);
        }
    }

    public sealed class Service
    {
        public void NoBaseCall(DerivedWithoutBaseContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void WithBaseCall(DerivedWithBaseContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void DisabledAfterBase(DerivedDisablingContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void AttributesDisabled(AttributeOverrideContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task InheritedRealModelHookAndNestedConfigurationStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public class InheritedProtectedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class InheritingContext : InheritedProtectedContext { }

    public sealed class HidingContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected new void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Status).IsConcurrencyToken();
        }
    }

    public sealed class NestedConfigurationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            void Configure()
            {
                modelBuilder.Entity<Order>().Property(x => x.Status).IsConcurrencyToken();
            }

            Action configure = () =>
                modelBuilder.Entity<Order>().Property(x => x.Name).IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Inherited(InheritingContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Hidden(HidingContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Nested(NestedConfigurationContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task AttributeAndFluentKeylessModelsStayQuietUnlessRekeyed()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    [Keyless]
    public sealed class AttributeView
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class FluentView
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class KeylessContext : DbContext
    {
        public DbSet<AttributeView> AttributeViews { get; set; }
        public DbSet<FluentView> FluentViews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentView>().HasNoKey();
        }
    }

    public sealed class RekeyedContext : DbContext
    {
        public DbSet<FluentView> FluentViews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentView>().HasNoKey();
            modelBuilder.Entity<FluentView>().HasKey(x => x.Id);
        }
    }

    public sealed class Service
    {
        public void Attribute(KeylessContext db)
        {
            var row = db.AttributeViews.First();
            row.Quantity++;
            db.SaveChanges();
        }

        public void Fluent(KeylessContext db)
        {
            var row = db.FluentViews.First();
            row.Quantity++;
            db.SaveChanges();
        }

        public void Rekeyed(RekeyedContext db)
        {
            var row = db.FluentViews.First();
            {|LC048:row.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task DeleteClearAddedAndPostMutationAttachDoNotReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Removed(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Remove(order);
            db.SaveChanges();
        }

        public void Deleted(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).State = EntityState.Deleted;
            db.SaveChanges();
        }

        public void Cleared(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.ChangeTracker.Clear();
            db.SaveChanges();
        }

        public void Accepted(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.ChangeTracker.AcceptAllChanges();
            db.SaveChanges();
        }

        public void OtherContextAccepts(AppDbContext db, AppDbContext other)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            other.ChangeTracker.AcceptAllChanges();
            db.SaveChanges();
        }

        public void ConditionalAcceptReports(AppDbContext db, bool accept)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            if (accept)
                db.ChangeTracker.AcceptAllChanges();
            db.SaveChanges();
        }

        public void OverloadedAcceptReports(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.AcceptAllChanges(0);
            db.SaveChanges();
        }

        public void HiddenAcceptReports(HiddenDetachmentContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.AcceptAllChanges();
            db.SaveChanges();
        }

        public void Added(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).State = EntityState.Added;
            db.SaveChanges();
        }

        public void AttachAfterMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            db.Entry(order).State = EntityState.Detached;
            order.Quantity++;
            db.Attach(order);
            db.SaveChanges();
        }

        public void AttachRangeBeforeMutation(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            db.AttachRange(order);
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UpdateRangeAfterMutation(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.UpdateRange(order);
            db.SaveChanges();
        }

        public void OverloadedClear(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.Clear(0);
            db.SaveChanges();
        }

        public void HiddenClear(HiddenDetachmentContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.Clear();
            db.SaveChanges();
        }

        public void HiddenDetachedState(HiddenDetachmentContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).State = EntityState.Detached;
            db.SaveChanges();
        }

        public void HiddenGenuineClear(HiddenGenuineTrackerContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.Clear();
            db.SaveChanges();
        }

        public void OverrideDetachedState(OverrideDetachmentContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).State = EntityState.Detached;
            db.SaveChanges();
        }
    }

    public sealed class HiddenDetachmentContext : DbContext
    {
        public new HiddenChangeTracker ChangeTracker { get; } = new HiddenChangeTracker();
        public DbSet<Order> Orders { get; set; }
        public new HiddenEntityEntry<T> Entry<T>(T entity) where T : class =>
            new HiddenEntityEntry<T>();
    }

    public sealed class HiddenChangeTracker
    {
        public void Clear() { }
        public void AcceptAllChanges() { }
    }

    public sealed class HiddenEntityEntry<T> where T : class
    {
        public EntityState State { get; set; }
    }

    public sealed class HiddenGenuineTrackerContext : DbContext
    {
        public new Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker =>
            base.ChangeTracker;
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class OverrideDetachmentContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(
            T entity) => base.Entry(entity);
    }
}
"""
        );
    }

    [Fact]
    public async Task NameofComputedPropertyAndUnrelatedContextReassignmentStayQuietOrReport()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class ComputedOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.DatabaseGenerated(
            System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Computed)]
        public int ComputedTotal { get; set; }

        public int Quantity { get; set; }
        public string Name { get; set; }
    }

    public sealed class ComputedContext : DbContext
    {
        public DbSet<ComputedOrder> Orders { get; set; }
    }

    public sealed class Service
    {
        public void Update(
            ComputedContext db,
            ComputedContext unrelated,
            ComputedContext replacement)
        {
            var order = db.Orders.First();
            order.Name = nameof(order.Name);
            order.ComputedTotal++;
            unrelated = replacement;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Helper(ComputedContext db)
        {
            var order = db.Orders.First();
            Apply(order);
            db.SaveChanges();
        }

        private static void Apply(ComputedOrder order)
        {
            order.Name = nameof(order.Name);
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task UnmappedConcurrencyAttributeAndGuardedFluentTokenReport()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class UnmappedTokenOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int IgnoredToken { get; set; }
    }

    public sealed class UnmappedTokenContext : DbContext
    {
        public DbSet<UnmappedTokenOrder> Orders { get; set; }
    }

    public sealed class GuardedContext : DbContext
    {
        private readonly bool _enableToken;
        public DbSet<UnmappedTokenOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (!_enableToken)
                return;

            modelBuilder
                .Entity<UnmappedTokenOrder>()
                .Property(x => x.Quantity)
                .IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Unmapped(UnmappedTokenContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Guarded(GuardedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ContextNoTrackingAndNamedSetRootsStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void ContextNoTracking(AppDbContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitTracking(AppDbContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var order = db.Orders.AsTracking().First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ExplicitNoTrackingOverload(AppDbContext db)
        {
            var order = db.Orders.AsTracking(QueryTrackingBehavior.NoTracking).First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitIdentityResolutionOverload(AppDbContext db)
        {
            var order = db.Orders
                .AsTracking(QueryTrackingBehavior.NoTrackingWithIdentityResolution)
                .First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitTrackAllOverload(AppDbContext db)
        {
            var order = db.Orders.AsTracking(QueryTrackingBehavior.TrackAll).First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UnknownTrackingOverload(
            AppDbContext db,
            QueryTrackingBehavior behavior)
        {
            var order = db.Orders.AsTracking(behavior).First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeTrackingOverload(AppDbContext db)
        {
            var order = QueryLookalikes
                .AsTracking(db.Orders, QueryTrackingBehavior.NoTracking)
                .First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void InvalidNamedSet(AppDbContext db)
        {
            var order = db.Set<Order>(" ").First();
            order.Quantity++;
            db.SaveChanges();
        }
    }

    public static class QueryLookalikes
    {
        public static IQueryable<T> AsTracking<T>(
            IQueryable<T> source,
            QueryTrackingBehavior behavior) => source;
    }
}
"""
        );
    }

    [Fact]
    public async Task UnconditionalAndNamedFluentTokensStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class UnrelatedControlFlowContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.Now.Ticks > 0)
            {
                _ = DateTime.UtcNow;
            }

            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class NamedPropertyContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property<byte[]>("ShadowVersion")
                .IsRowVersion();
        }
    }

    public sealed class Service
    {
        public void UnrelatedControlFlow(UnrelatedControlFlowContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NamedProperty(NamedPropertyContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task UnchangedAndThrowingOverwritePreservePersistedMutationEvidence()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class ThrowingSetterOrder
    {
        private int quantity;

        public int Id { get; set; }
        public int Quantity
        {
            get => quantity;
            set
            {
                if (value == 42)
                    throw new InvalidOperationException();
                quantity = value;
            }
        }
    }

    public sealed class FieldLikeSetterOrder
    {
        private int quantity;

        public int Id { get; set; }
        public int Quantity
        {
            get => quantity;
            set => quantity = value;
        }
    }

    public sealed class SetterContext : DbContext
    {
        public DbSet<ThrowingSetterOrder> Throwing { get; set; }
        public DbSet<FieldLikeSetterOrder> FieldLike { get; set; }
    }

    public sealed class Service
    {
        public void TrackedUnchangedBeforeMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            db.Entry(order).State = EntityState.Unchanged;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void NoTrackingUnchangedBeforeMutation(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            db.Entry(order).State = EntityState.Unchanged;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UnchangedAfterMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).State = EntityState.Unchanged;
            db.SaveChanges();
        }

        public void NoTrackingUnchangedAfterMutation(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            db.Entry(order).State = EntityState.Unchanged;
            order.Quantity++;
            db.Entry(order).State = EntityState.Unchanged;
            db.SaveChanges();
        }

        public void OtherEntityDoesNotReattach(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            var other = db.Orders.AsNoTracking().Last();
            db.Entry(other).State = EntityState.Unchanged;
            order.Quantity++;
            db.SaveChanges();
        }

        public void OtherContextDoesNotReattach(AppDbContext db, AppDbContext other)
        {
            var order = db.Orders.AsNoTracking().First();
            other.Entry(order).State = EntityState.Unchanged;
            order.Quantity++;
            db.SaveChanges();
        }

        public void ConditionalUnchangedReportsReachableReattachment(AppDbContext db, bool reattach)
        {
            var order = db.Orders.AsNoTracking().First();
            if (reattach)
                db.Entry(order).State = EntityState.Unchanged;

            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeEntryDoesNotReattach(LookalikeEntryContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            db.Entry(order).State = EntityState.Unchanged;
            order.Quantity++;
            db.SaveChanges();
        }

        public void RealEntryOverrideReattaches(OverrideEntryContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            db.Entry(order).State = EntityState.Unchanged;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ThrowingOverwrite(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                {|LC048:order.Quantity|}++;
                order.Quantity = Compute();
            }
            catch (InvalidOperationException)
            {
            }

            db.SaveChanges();
        }

        public void ThrowingSetterWithContinuingCatch(SetterContext db)
        {
            var order = db.Throwing.First();
            try
            {
                {|LC048:order.Quantity|}++;
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            db.SaveChanges();
        }

        public void ThrowingSetterWithCatchSave(SetterContext db)
        {
            var order = db.Throwing.First();
            try
            {
                {|LC048:order.Quantity|}++;
                order.Quantity = 42;
                db.SaveChanges();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void StraightLineThrowingSetterCompletesBeforeSave(SetterContext db)
        {
            var order = db.Throwing.First();
            order.Quantity++;
            order.Quantity = 42;
            db.SaveChanges();
        }

        public void AutoSetterIsProvenNonThrowing(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            db.SaveChanges();
        }

        public void FieldLikeSetterIsProvenNonThrowing(SetterContext db)
        {
            var order = db.FieldLike.First();
            try
            {
                order.Quantity++;
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            db.SaveChanges();
        }

        public void ThrowingInitializerSetterWithContinuingCatch(SetterContext db)
        {
            var order = db.Throwing.First();
            try
            {
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ThrowingInitializerRhsWithContinuingCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity = Compute();
            }
            catch (InvalidOperationException)
            {
            }

            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ThrowingInitializerSetterBeforeCapturedRead(SetterContext db)
        {
            var order = db.Throwing.First();
            try
            {
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            var loaded = order.Quantity;
            {|LC048:order.Quantity|} = loaded + 1;
            db.SaveChanges();
        }

        public void StraightLineThrowingInitializerCompletes(SetterContext db)
        {
            var order = db.Throwing.First();
            order.Quantity = 42;
            order.Quantity++;
            db.SaveChanges();
        }

        public void AutoInitializerSetterIsProvenNonThrowing(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            order.Quantity++;
            db.SaveChanges();
        }

        public void FieldLikeInitializerSetterIsProvenNonThrowing(SetterContext db)
        {
            var order = db.FieldLike.First();
            try
            {
                order.Quantity = 42;
            }
            catch (InvalidOperationException)
            {
            }

            order.Quantity++;
            db.SaveChanges();
        }

        private static int Compute() => throw new InvalidOperationException();
    }

    public sealed class LookalikeEntryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new LookalikeEntityEntry<T> Entry<T>(T entity) where T : class =>
            new LookalikeEntityEntry<T>();
    }

    public sealed class LookalikeEntityEntry<T> where T : class
    {
        public EntityState State { get; set; }
    }

    public sealed class OverrideEntryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(
            T entity) => base.Entry(entity);
    }
}
"""
        );
    }

    [Fact]
    public async Task CallbackKeylessFluentIgnoreAndExclusiveReattachmentStayQuiet()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class ViewRow
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class CallbackKeylessContext : DbContext
    {
        public DbSet<ViewRow> Rows { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ViewRow>(builder => builder.HasNoKey());
        }
    }

    public sealed class IgnoredPropertyContext : DbContext
    {
        public DbSet<ViewRow> Rows { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ViewRow>().Ignore(x => x.Quantity);
        }
    }

    public sealed class Service
    {
        public void Keyless(CallbackKeylessContext db)
        {
            var row = db.Rows.First();
            row.Quantity++;
            db.SaveChanges();
        }

        public void Ignored(IgnoredPropertyContext db)
        {
            var row = db.Rows.First();
            row.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );

        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Exclusive(AppDbContext db, bool detach)
        {
            var order = db.Orders.First();
            if (detach)
            {
                db.Entry(order).State = EntityState.Detached;
                order.Quantity++;
            }
            else
            {
                db.Update(order);
            }

            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConfigureAwaitAndThrowingAwaitOverwriteReport()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class ConfigureAwaitEntity
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        public ConfigureAwaitEntity ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfigureAwaitEntity();
    }

    public sealed class ConfigureAwaitEntityContext : DbContext
    {
        public DbSet<ConfigureAwaitEntity> Orders { get; set; }
    }

    public sealed class Service
    {
        public async Task ConfigureAwaitTerminal(AppDbContext db)
        {
            var order = await db.Orders.FirstAsync().ConfigureAwait(false);
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }
        public void BlockingTaskResults(AppDbContext db)
        {
            var result = db.Orders.FirstAsync().Result;
            {|LC048:result.Quantity|}++;
            db.SaveChanges();

            var awaiter = db.Set<Order>().SingleOrDefaultAsync().GetAwaiter().GetResult();
            {|LC048:awaiter.Status|}++;
            db.SaveChanges();

            var configured = db.Orders.FirstAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            {|LC048:configured.Quantity|}++;
            db.SaveChanges();
        }

        public void BlockingStableTaskLocal(AppDbContext db)
        {
            var pending = db.Orders.FirstAsync();
            var order = pending.Result;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BlockingValueTaskResults(AppDbContext db)
        {
            var result = db.Orders.LastAsync().Result;
            {|LC048:result.Quantity|}++;
            db.SaveChanges();

            var configured = db.Orders.LastAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            {|LC048:configured.Status|}++;
            db.SaveChanges();
        }

        public void BlockingFindAndTrackingOrigins(AppDbContext db)
        {
            var found = db.FindAsync<Order>(1).Result;
            {|LC048:found.Quantity|}++;
            db.SaveChanges();

            var untracked = db.Orders.AsNoTracking().FirstAsync().Result;
            untracked.Quantity++;
            db.SaveChanges();
        }

        public void BlockingLookalikesAndCollectionsStayQuiet(AppDbContext db)
        {
            var customResult = new ResultLookalike<Order>(db.Orders.First()).Result;
            customResult.Quantity++;

            var customAwaiter = new AwaitableLookalike<Order>(db.Orders.First())
                .GetAwaiter()
                .GetResult();
            customAwaiter.Quantity++;

            var configured = AwaitLookalike.ConfigureAwait(
                    db.Orders.FirstAsync(),
                    false)
                .GetAwaiter()
                .GetResult();
            configured.Quantity++;

            var collection = db.Orders.ToListAsync().Result.First();
            collection.Quantity++;

            _ = db.Orders.FirstAsync();
            db.SaveChanges();
        }

        public async Task ThrowingOverwrite(AppDbContext db)
        {
            var order = db.Orders.First();
            var replacement = Task.FromException<int>(new InvalidOperationException());
            try
            {
                {|LC048:order.Quantity|}++;
                order.Quantity = await replacement;
            }
            catch (InvalidOperationException)
            {
            }

            await db.SaveChangesAsync();
        }

        public void ThrowExpressionNullGuard(AppDbContext db)
        {
            var order = db.Orders.SingleOrDefault(x => x.Id == 1)
                ?? throw new InvalidOperationException();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ProvenThrowNullGuard(AppDbContext db)
        {
            var order = db.Orders.SingleOrDefault(x => x.Id == 2) ?? Missing();
            {|LC048:order.Status|}++;
            db.SaveChanges();
        }

        public void CoalescedReplacement(AppDbContext db)
        {
            var order = db.Orders.SingleOrDefault(x => x.Id == 3) ?? new Order();
            order.Quantity++;
            db.SaveChanges();
        }

        public async Task ProjectConfigureAwaitReplacement(AppDbContext db)
        {
            var order = await AwaitLookalike.ConfigureAwait(db.Orders.FirstAsync(), false);
            order.Quantity++;
            await db.SaveChangesAsync();
        }

        public void EntityConfigureAwaitReplacement(ConfigureAwaitEntityContext db)
        {
            var order = db.Orders.First().ConfigureAwait(false);
            order.Quantity++;
            db.SaveChanges();
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        private static Order Missing() => throw new InvalidOperationException();
    }

    public static class AwaitLookalike
    {
        public static Task<T> ConfigureAwait<T>(Task<T> source, bool continueOnCapturedContext) =>
            Task.FromResult(default(T));
    }

    public sealed class ResultLookalike<T>
    {
        public ResultLookalike(T result) => Result = result;
        public T Result { get; }
    }

    public sealed class AwaitableLookalike<T>
    {
        private readonly T _result;

        public AwaitableLookalike(T result) => _result = result;
        public AwaiterLookalike<T> GetAwaiter() => new AwaiterLookalike<T>(_result);
    }

    public sealed class AwaiterLookalike<T>
    {
        private readonly T _result;

        public AwaiterLookalike(T result) => _result = result;
        public T GetResult() => _result;
    }
}
"""
        );
    }

    [Fact]
    public async Task HelperOrderingRebindingAndBlindOverwriteStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void SaveMutateSave(AppDbContext db)
        {
            var order = db.Orders.First();
            ApplyBetweenSaves(order, db);
        }

        public void Rebound(AppDbContext db)
        {
            var order = db.Orders.First();
            RebindThenSave(ref order, db);
        }

        public void BlindOverwrite(AppDbContext db)
        {
            var order = db.Orders.First();
            OverwriteThenSave(order, db);
        }

        private static void ApplyBetweenSaves(Order order, AppDbContext db)
        {
            db.SaveChanges();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        private static void RebindThenSave(ref Order order, AppDbContext db)
        {
            Replace(ref order);
            order.Quantity++;
            db.SaveChanges();
        }

        private static void OverwriteThenSave(Order order, AppDbContext db)
        {
            order.Quantity++;
            order.Quantity = 0;
            db.SaveChanges();
        }

        private static void Replace(ref Order order)
        {
            order = new Order();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task EachHelperMutationPairsOnlyWithItsFollowingSave()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.First();
            MutateSaveMutate(order, db);
        }

        private static void MutateSaveMutate(Order order, AppDbContext db)
        {
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
            order.Status++;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task IgnoredTokensAndConditionalCallbackTokensDoNotSuppress()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class TokenOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int Version { get; set; }
    }

    public sealed class PlainOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public int Version { get; set; }
    }

    public sealed class AttributedIgnoredContext : DbContext
    {
        public DbSet<TokenOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TokenOrder>().Ignore(x => x.Version);
        }
    }

    public sealed class FluentIgnoredContext : DbContext
    {
        public DbSet<TokenOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<TokenOrder>()
                .Property(x => x.Version)
                .IsConcurrencyToken();
            modelBuilder.Entity<TokenOrder>().Ignore(x => x.Version);
        }
    }

    public sealed class ConditionalCallbackContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainOrder>(builder =>
            {
                if (DateTime.UtcNow.Ticks > 0)
                {
                    builder.Property(x => x.Version).IsConcurrencyToken();
                }
            });
        }
    }

    public sealed class NotMappedOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Quantity { get; set; }
    }

    public sealed class GuaranteedConcurrencyContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (true)
                modelBuilder
                    .Entity<PlainOrder>()
                    .Property(x => x.Quantity)
                    .IsConcurrencyToken();
        }
    }

    public sealed class GuaranteedRowVersionContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainOrder>(builder =>
            {
                _ = true
                    ? builder.Property(x => x.Version).IsRowVersion()
                    : null;
            });
        }
    }

    public sealed class GuaranteedKeyContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (true)
                modelBuilder.Entity<PlainOrder>().HasKey(x => x.Quantity);
        }
    }

    public sealed class GuaranteedKeylessContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ = 1 switch
            {
                1 => modelBuilder.Entity<PlainOrder>().HasNoKey(),
                _ => null,
            };
        }
    }

    public sealed class GuaranteedIgnoreContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (true || DateTime.UtcNow.Ticks > 0)
                modelBuilder.Entity<PlainOrder>().Ignore(x => x.Quantity);
        }
    }

    public sealed class GuaranteedMapContext : DbContext
    {
        public DbSet<NotMappedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NotMappedOrder>().Ignore(x => x.Quantity);
            if (true)
                modelBuilder.Entity<NotMappedOrder>().Property(x => x.Quantity);
        }
    }

    public sealed class GuaranteedDisableContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<PlainOrder>()
                .Property(x => x.Quantity)
                .IsConcurrencyToken();
            if (true)
                modelBuilder
                    .Entity<PlainOrder>()
                    .Property(x => x.Quantity)
                    .IsConcurrencyToken(false);
        }
    }

    public sealed class DeadBranchContext : DbContext
    {
        public DbSet<PlainOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (false)
                modelBuilder
                    .Entity<PlainOrder>()
                    .Property(x => x.Quantity)
                    .IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Attributed(AttributedIgnoredContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Fluent(FluentIgnoredContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalCallback(ConditionalCallbackContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
        public void GuaranteedConcurrency(GuaranteedConcurrencyContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedRowVersion(GuaranteedRowVersionContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedKey(GuaranteedKeyContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedKeyless(GuaranteedKeylessContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedIgnore(GuaranteedIgnoreContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedMap(GuaranteedMapContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GuaranteedDisable(GuaranteedDisableContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void DeadBranch(DeadBranchContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ShadowIgnoreAndLaterPropertyMappingStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class ShadowIgnoredContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property<byte[]>("Version")
                .IsRowVersion();
            modelBuilder.Entity<Order>().Ignore("Version");
        }
    }

    public sealed class RemappedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Ignore(x => x.Quantity);
            modelBuilder.Entity<Order>().Property(x => x.Quantity);
        }
    }

    public sealed class Service
    {
        public void ShadowIgnored(ShadowIgnoredContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Remapped(RemappedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task SwitchExpressionHelperAndNonReachingTrackingAssignmentStayQuiet()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void SwitchHelper(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            Apply(order, db, mutate);
        }

        public void CompileTimeDeadHelperEffects(AppDbContext db)
        {
            var order = db.Orders.First();
            DeadShortCircuitMutation(order, db);
            DeadCoalesceMutation(order, db);
            DeadShortCircuitSave(order, db);
            DeadCoalesceSave(order, db);
        }

        public void TrackingMode(AppDbContext db, bool stop)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (stop)
            {
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                return;
            }

            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        private static int Apply(Order order, AppDbContext db, bool mutate) =>
            mutate switch
            {
                true => order.Quantity++,
                false => db.SaveChanges(),
            };

        private static void DeadShortCircuitMutation(Order order, AppDbContext db)
        {
            _ = false && order.Quantity++ > 0;
            db.SaveChanges();
        }

        private static void DeadShortCircuitSave(Order order, AppDbContext db)
        {
            order.Quantity++;
            _ = true || db.SaveChanges() > 0;
        }

        private static void DeadCoalesceMutation(Order order, AppDbContext db)
        {
            _ = "live" ?? (object)(order.Quantity += 1);
            db.SaveChanges();
        }

        private static void DeadCoalesceSave(Order order, AppDbContext db)
        {
            order.Quantity++;
            _ = "live" ?? (object)db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConditionalOuterCallbackDoesNotProveConcurrency()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class ConditionalOuterContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.UtcNow.Ticks > 0)
            {
                modelBuilder.Entity<Order>(builder =>
                    builder.Property(x => x.Status).IsConcurrencyToken());
            }
        }
    }

    public sealed class Service
    {
        public void Update(ConditionalOuterContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ReloadAndObservedReloadAsyncResetPendingMutations()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Reload(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).Reload();
            db.SaveChanges();
        }

        public async Task ReloadAsync(AppDbContext db)
        {
            var awaited = await db.Orders.FirstAsync();
            awaited.Quantity++;
            await db.Entry(awaited).ReloadAsync();
            await db.SaveChangesAsync();

            var unobserved = await db.Orders.FirstAsync();
            {|LC048:unobserved.Quantity|}++;
            _ = db.Entry(unobserved).ReloadAsync();
            await db.SaveChangesAsync();

            var configuredAwait = await db.Orders.FirstAsync();
            configuredAwait.Quantity++;
            await db.Entry(configuredAwait).ReloadAsync().ConfigureAwait(false);
            await db.SaveChangesAsync();
        }

        public void OverloadedReload(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Reload(0);
            db.SaveChanges();
        }

        public void HiddenReload(HiddenReloadContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Reload();
            db.SaveChanges();
        }

        public void OverrideReload(OverrideReloadContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).Reload();
            db.SaveChanges();
        }
    }

    public sealed class HiddenReloadContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new HiddenReloadEntry<T> Entry<T>(T entity) where T : class =>
            new HiddenReloadEntry<T>();
    }

    public sealed class HiddenReloadEntry<T> where T : class
    {
        public void Reload() { }
    }

    public sealed class OverrideReloadContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(
            T entity) => base.Entry(entity);
    }
}
"""
        );
    }

    [Fact]
    public async Task HelperSaveUsesSaveChangesAsAdditionalLocation()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.First();
            Adjust(order);
            Persist(db);
        }

        private static void Adjust(Order order)
        {
            {|#0:order.Quantity|}++;
        }

        private static void Persist(AppDbContext db)
        {
            {|#1:db.SaveChanges()|};
        }
    }
}
""",
            new DiagnosticResult(
                LostUpdateRiskAnalyzer.DiagnosticId,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
            )
                .WithLocation(0)
                .WithLocation(1)
                .WithArguments("Quantity")
        );
    }

    [Fact]
    public async Task UnrelatedConcurrencyTokensDoNotSuppress()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class CheckedOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int Status { get; set; }
    }

    public sealed class CheckedContext : DbContext
    {
        public DbSet<CheckedOrder> Orders { get; set; }
    }

    public sealed class FluentContext : DbContext
    {
        public DbSet<CheckedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<CheckedOrder>()
                .Property(x => x.Status)
                .IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Attribute(CheckedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Fluent(FluentContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CfgReachabilityAndEfChangeTrackerSymbolsStayCorrect()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db, bool repeat)
        {
            var order = db.Orders.First();
            while (repeat)
            {
                db.SaveChanges();
                {|LC048:order.Quantity|}++;
            }
        }

        public void ReachableBranch(AppDbContext db, bool save)
        {
            var order = db.Orders.First();
            {|LC048:order.Status|}++;
            if (save)
                db.SaveChanges();
        }

        public void ReachableCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                {|LC048:order.Quantity|}++;
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void CompileTimeDeadSave(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            while (false)
                db.SaveChanges();
        }

        public void CompileTimeDeadMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            while (false)
                order.Status++;
            db.SaveChanges();
        }

        public void TerminalSave(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            return;
            db.SaveChanges();
        }

        public void HiddenRealTracker(HiddenTrackerContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeTracker(LookalikeTrackerContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Status|}++;
            db.SaveChanges();
        }
    }

    public sealed class HiddenTrackerContext : DbContext
    {
        public new Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker =>
            base.ChangeTracker;
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class LookalikeTracker
    {
        public QueryTrackingBehavior QueryTrackingBehavior { get; set; }
        public bool AutoDetectChangesEnabled { get; set; }
    }

    public sealed class LookalikeTrackerContext : DbContext
    {
        public new LookalikeTracker ChangeTracker { get; } = new LookalikeTracker();
        public DbSet<Order> Orders { get; set; }
}
}
"""
        );
    }

    [Fact]
    public async Task FluentRemappingOverridesNotMappedAndConditionalRemapInvalidatesIgnore()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class RemappedOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Quantity { get; set; }
    }

    public sealed class RemappedContext : DbContext
    {
        public DbSet<RemappedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RemappedOrder>().Property(x => x.Quantity);
        }
    }

    public sealed class ConditionalContext : DbContext
    {
        public DbSet<RemappedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RemappedOrder>().Ignore(x => x.Quantity);
            if (DateTime.UtcNow.Ticks > 0)
            {
                modelBuilder.Entity<RemappedOrder>().Property(x => x.Quantity);
            }
        }
    }

    public sealed class Service
    {
        public void Remapped(RemappedContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Conditional(ConditionalContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task PlainShadowConcurrencyTokenDoesNotProtectOtherProperties()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class ShadowTokenContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .Property<int>("Status")
                .IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Update(ShadowTokenContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task MatchingCatchSaveReceivesThrownMutation()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                {|LC048:order.Quantity|}++;
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CatchSaveRequiresReachableMatchingThrowAfterMutation()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Reachable(AppDbContext db, bool fail)
        {
            var order = db.Orders.First();
            try
            {
                {|LC048:order.Quantity|}++;
                if (fail)
                    throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void ExclusiveBranch(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            try
            {
                if (mutate)
                {
                    order.Quantity++;
                    return;
                }

                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void WrongException(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
                throw new ArgumentException();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task FindAlwaysTracksDespiteContextNoTracking()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public async Task Update(AppDbContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            var synchronous = db.Orders.Find(1);
            {|LC048:synchronous.Quantity|}++;
            db.SaveChanges();

            var asynchronous = await db.Orders.FindAsync(2);
            {|LC048:asynchronous.Quantity|}++;
            await db.SaveChangesAsync();

            var query = db.Orders.First();
            query.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task MatchingIsModifiedAssignmentPersistsOnlyMatchingPropertyEntityAndContext()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Matching(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property(x => x.Quantity).IsModified = true;
            db.SaveChanges();
        }

        public void Named(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property("Quantity").IsModified = true;
            db.SaveChanges();
        }

        public void MatchingFalseCancelsPendingWrite(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Entry(order).Property(x => x.Quantity).IsModified = false;
            db.SaveChanges();
        }

        public void MismatchedFalseDoesNotCancel(
            AppDbContext db,
            AppDbContext other,
            bool reset)
        {
            var order = db.Orders.First();
            var otherOrder = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property(x => x.Status).IsModified = false;
            db.Entry(otherOrder).Property(x => x.Quantity).IsModified = false;
            other.Entry(order).Property(x => x.Quantity).IsModified = false;
            if (reset)
                db.Entry(order).Property(x => x.Quantity).IsModified = false;
            db.SaveChanges();
        }

        public void FalseBeforeMutationDoesNotCancel(AppDbContext db)
        {
            var order = db.Orders.First();
            db.Entry(order).Property("Quantity").IsModified = false;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LaterTrueRestoresPendingWrite(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property("Quantity").IsModified = false;
            db.Entry(order).Property("Quantity").IsModified = true;
            db.SaveChanges();
        }

        public void HiddenFalseDoesNotCancel(HiddenEntryContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property("Quantity").IsModified = false;
            db.SaveChanges();
        }

        public void Mismatches(AppDbContext db, AppDbContext other)
        {
            var order = db.Orders.AsNoTracking().First();
            var otherOrder = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).Property(x => x.Status).IsModified = true;
            db.Entry(otherOrder).Property(x => x.Quantity).IsModified = true;
            other.Entry(order).Property(x => x.Quantity).IsModified = true;
            db.SaveChanges();
        }

        public void ConstantFoldedName(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property("Quan" + "tity").IsModified = true;
            db.SaveChanges();
        }

        public void ConcatenatedDescendantToken(AppDbContext db, string suffix)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).Property("Quantity" + suffix).IsModified = true;
            db.SaveChanges();
        }

        public void NonExactLambda(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).Property(x => x.Quantity + x.Status).IsModified = true;
            db.SaveChanges();
        }

        public void OverloadedProperty(AppDbContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).Property(0).IsModified = true;
            db.SaveChanges();
        }

        public void HiddenEntryMembers(HiddenEntryContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.Entry(order).Property("Quantity").IsModified = true;
            db.SaveChanges();
        }

        public void ValidOverride(OverrideEntryContext db)
        {
            var order = db.Orders.AsNoTracking().First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).Property("Quantity").IsModified = true;
            db.SaveChanges();
        }
    }

    public sealed class HiddenEntryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new HiddenPropertyEntityEntry<T> Entry<T>(T entity) where T : class =>
            new HiddenPropertyEntityEntry<T>();
    }

    public sealed class HiddenPropertyEntityEntry<T> where T : class
    {
        public HiddenPropertyEntry Property(string propertyName) => new HiddenPropertyEntry();
    }

    public sealed class HiddenPropertyEntry
    {
        public bool IsModified { get; set; }
    }

    public sealed class OverrideEntryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Entry<T>(
            T entity) => base.Entry(entity);
    }
}
"""
        );
    }

    [Fact]
    public async Task DisabledAutoDetectionNeedsExplicitPersistenceProof()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Disabled(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Detected(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }

        public void Modified(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Entry(order).State = EntityState.Modified;
            db.SaveChanges();
        }

        public void Unrelated(AppDbContext db, AppDbContext other)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            other.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task EffectivePrimaryAndAlternateKeyMutationsStayQuiet()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class Conventional
    {
        public int Id { get; set; }
        public int ConventionalId { get; set; }
        public int Quantity { get; set; }
    }

    public class BaseConvention
    {
        public int iD { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class DerivedConvention : BaseConvention
    {
        public int Id { get; set; }
        public int DerivedConventionId { get; set; }
    }

    public class NamedRootConvention
    {
        public int NamedRootConventionId { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class NamedDerivedConvention : NamedRootConvention
    {
        public int Id { get; set; }
        public int NamedDerivedConventionId { get; set; }
    }

    public sealed class FallbackConvention
    {
        public int FALLBACKCONVENTIONID { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class Attributed
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class Fluent
    {
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<Conventional> Conventionals { get; set; }
        public DbSet<Attributed> Attributeds { get; set; }
        public DbSet<Fluent> Fluents { get; set; }
        public DbSet<DerivedConvention> DerivedConventions { get; set; }
        public DbSet<NamedDerivedConvention> NamedDerivedConventions { get; set; }
        public DbSet<FallbackConvention> FallbackConventions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Fluent>().HasNoKey();
            modelBuilder.Entity<Fluent>().HasKey(x => x.Code);
        }
    }

    public sealed class Service
    {
        public void Update(KeyContext db)
        {
            var conventional = db.Conventionals.First();
            conventional.Id++;
            {|LC048:conventional.ConventionalId|}++;
            {|LC048:conventional.Quantity|}++;

            var attributed = db.Attributeds.First();
            attributed.Code += "x";
            {|LC048:attributed.Quantity|}++;

            var fluent = db.Fluents.First();
            fluent.Code += "x";
            {|LC048:fluent.Quantity|}++;

            var derived = db.DerivedConventions.First();
            derived.iD++;
            {|LC048:derived.Id|}++;
            {|LC048:derived.DerivedConventionId|}++;
            {|LC048:derived.Quantity|}++;

            var namedDerived = db.NamedDerivedConventions.First();
            namedDerived.NamedRootConventionId++;
            {|LC048:namedDerived.Id|}++;
            {|LC048:namedDerived.NamedDerivedConventionId|}++;
            {|LC048:namedDerived.Quantity|}++;

            var fallback = db.FallbackConventions.First();
            fallback.FALLBACKCONVENTIONID++;
            {|LC048:fallback.Quantity|}++;

            db.SaveChanges();
        }
    }

    public class AlternateKeyRoot
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string Code { get; set; }
        public int BranchCode { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class RootConfiguredOrder : AlternateKeyRoot { }
    public sealed class LambdaAlternateOrder : AlternateKeyRoot { }
    public sealed class AliasAlternateOrder : AlternateKeyRoot { }
    public sealed class CompositeAlternateOrder : AlternateKeyRoot { }

    public class BaseAlternateKeyContext : DbContext
    {
        public DbSet<RootConfiguredOrder> RootConfiguredOrders { get; set; }
        public DbSet<LambdaAlternateOrder> LambdaAlternateOrders { get; set; }
        public DbSet<AliasAlternateOrder> AliasAlternateOrders { get; set; }
        public DbSet<CompositeAlternateOrder> CompositeAlternateOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AlternateKeyRoot>().HasAlternateKey(x => x.Code);
            modelBuilder.Entity<LambdaAlternateOrder>()
                .HasAlternateKey(x => new { x.TenantId, x.BranchCode });

            var stableModelBuilder = modelBuilder;
            var entityBuilder = stableModelBuilder.Entity<AliasAlternateOrder>();
            var stableEntityBuilder = entityBuilder;
            stableEntityBuilder.HasAlternateKey(nameof(AliasAlternateOrder.TenantId));

            if (true)
                modelBuilder.Entity<AliasAlternateOrder>().HasAlternateKey(x => x.BranchCode);
        }
    }

    public sealed class BaseAfterAlternateKeyContext : BaseAlternateKeyContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CompositeAlternateOrder>()
                .HasAlternateKey("TenantId", nameof(CompositeAlternateOrder.BranchCode));
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class BaseBeforeAlternateKeyContext : BaseAlternateKeyContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CompositeAlternateOrder>()
                .HasAlternateKey(x => new { x.TenantId, x.BranchCode });
        }
    }

    public sealed class AlternateKeyService
    {
        public void RootConfiguration(BaseAfterAlternateKeyContext db)
        {
            var order = db.RootConfiguredOrders.First();
            order.Code += "x";
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LambdaComposite(BaseAfterAlternateKeyContext db)
        {
            var order = db.LambdaAlternateOrders.First();
            order.TenantId++;
            order.BranchCode++;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void StableAliasesAndGuaranteedBranch(BaseAfterAlternateKeyContext db)
        {
            var order = db.AliasAlternateOrders.First();
            order.TenantId++;
            order.BranchCode++;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BaseAfter(BaseAfterAlternateKeyContext db)
        {
            var order = db.CompositeAlternateOrders.First();
            order.Code += "x";
            order.TenantId++;
            order.BranchCode++;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void BaseBefore(BaseBeforeAlternateKeyContext db)
        {
            var order = db.CompositeAlternateOrders.First();
            order.Code += "x";
            order.TenantId++;
            order.BranchCode++;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task DefiniteBlindOverwriteAcrossBackedgeSuppressesOnlyDefinitePath()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Definite(AppDbContext db, bool repeat)
        {
            var order = db.Orders.First();
            while (repeat)
            {
                order.Quantity = 0;
                db.SaveChanges();
                order.Quantity++;
            }
        }

        public void Conditional(AppDbContext db, bool repeat, bool overwrite)
        {
            var order = db.Orders.First();
            while (repeat)
            {
                if (overwrite)
                    order.Quantity = 0;
                db.SaveChanges();
                {|LC048:order.Quantity|}++;
            }
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CompileTimeDeadExpressionBranchesDoNotProvidePropertyReads()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Dead(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = true ? 0 : order.Quantity;
            order.Name = "fixed" ?? order.Name;
            order.Status = 1 switch { 1 => 0, _ => order.Status };
            db.SaveChanges();
        }

        public void Live(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = false ? 0 : order.Quantity;
            {|LC048:order.Name|} = ((string)null) ?? order.Name;
            {|LC048:order.Status|} = 2 switch { 1 => 0, _ => order.Status };
            db.SaveChanges();
        }
        public void UnknownGuard(AppDbContext db, bool flag)
        {
            var order = db.Orders.First();
            {|LC048:order.Status|} = 0 switch
            {
                0 when flag => 1,
                _ => order.Status,
            };
            db.SaveChanges();
        }

    }
}
"""
        );
    }

    [Fact]
    public async Task ExplicitKeysOverrideConventionalKeyNames()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class AttributedOrder
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Key]
        public string Code { get; set; }

        public int Quantity { get; set; }
    }

    public sealed class FluentOrder
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<AttributedOrder> AttributedOrders { get; set; }
        public DbSet<FluentOrder> FluentOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentOrder>().HasKey(x => x.Code);
        }
    }

    public sealed class Service
    {
        public void Update(KeyContext db)
        {
            var attributed = db.AttributedOrders.First();
            {|LC048:attributed.Id|}++;
            attributed.Code += "x";
            {|LC048:attributed.Quantity|}++;

            var fluent = db.FluentOrders.First();
            {|LC048:fluent.Id|}++;
            fluent.Code += "x";
            {|LC048:fluent.Quantity|}++;

            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ExplicitUpdatesPersistWhenAutoDetectionIsDisabled()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class DelegatingStateContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update<TEntity>(TEntity entity) => base.Update(entity);
        public override void UpdateRange(params object[] entities) =>
            base.UpdateRange(entities);
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach<TEntity>(TEntity entity) => base.Attach(entity);
        public override void AttachRange(params object[] entities) =>
            base.AttachRange(entities);
    }

    public sealed class DelegatingStateDbSet<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update(TEntity entity) => base.Update(entity);
        public override void UpdateRange(params TEntity[] entities) =>
            base.UpdateRange(entities);
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach(TEntity entity) => base.Attach(entity);
        public override void AttachRange(params TEntity[] entities) =>
            base.AttachRange(entities);
    }

    public sealed class DelegatingStateSetContext : DbContext
    {
        public DelegatingStateDbSet<Order> Orders { get; set; }
    }

    public class HidingStateBaseContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update<TEntity>(TEntity entity) where TEntity : class =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void UpdateRange(params object[] entities) { }
        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach<TEntity>(TEntity entity) where TEntity : class =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void AttachRange(params object[] entities) { }
    }

    public sealed class InvalidStateOverrideContext : HidingStateBaseContext
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update<TEntity>(TEntity entity) => base.Update(entity);
        public override void UpdateRange(params object[] entities) =>
            base.UpdateRange(entities);
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach<TEntity>(TEntity entity) => base.Attach(entity);
        public override void AttachRange(params object[] entities) =>
            base.AttachRange(entities);
    }

    public class HidingStateDbSetBase<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update(TEntity entity) =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void UpdateRange(params TEntity[] entities) { }
        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach(TEntity entity) =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void AttachRange(params TEntity[] entities) { }
    }

    public sealed class InvalidStateOverrideDbSet<TEntity> : HidingStateDbSetBase<TEntity>
        where TEntity : class
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Update(TEntity entity) => base.Update(entity);
        public override void UpdateRange(params TEntity[] entities) =>
            base.UpdateRange(entities);
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Attach(TEntity entity) => base.Attach(entity);
        public override void AttachRange(params TEntity[] entities) =>
            base.AttachRange(entities);
    }

    public sealed class InvalidStateOverrideSetContext : DbContext
    {
        public InvalidStateOverrideDbSet<Order> Orders { get; set; }
    }

    public sealed class Service
    {
        public void ContextUpdate(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.Update(order);
            db.SaveChanges();
        }

        public void ContextUpdateRange(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.UpdateRange(order);
            db.SaveChanges();
        }

        public void SetUpdates(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var first = db.Orders.First();
            {|LC048:first.Quantity|}++;
            db.Orders.Update(first);
            db.SaveChanges();

            var second = db.Orders.First();
            {|LC048:second.Quantity|}++;
            db.Orders.UpdateRange(second);
            db.SaveChanges();
        }

        public void DelegatedContextOverrides(DelegatingStateContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var updated = db.Orders.First();
            {|LC048:updated.Quantity|}++;
            db.Update(updated);
            db.SaveChanges();

            var updatedRange = db.Orders.First();
            {|LC048:updatedRange.Quantity|}++;
            db.UpdateRange(updatedRange);
            db.SaveChanges();

            db.ChangeTracker.AutoDetectChangesEnabled = true;
            var attached = db.Orders.AsNoTracking().First();
            db.Attach(attached);
            {|LC048:attached.Quantity|}++;
            db.SaveChanges();

            var attachedRange = db.Orders.AsNoTracking().First();
            db.AttachRange(attachedRange);
            {|LC048:attachedRange.Quantity|}++;
            db.SaveChanges();
        }

        public void DelegatedSetOverrides(DelegatingStateSetContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var updated = db.Orders.First();
            {|LC048:updated.Quantity|}++;
            db.Orders.Update(updated);
            db.SaveChanges();

            var updatedRange = db.Orders.First();
            {|LC048:updatedRange.Quantity|}++;
            db.Orders.UpdateRange(updatedRange);
            db.SaveChanges();

            db.ChangeTracker.AutoDetectChangesEnabled = true;
            var attached = db.Orders.AsNoTracking().First();
            db.Orders.Attach(attached);
            {|LC048:attached.Quantity|}++;
            db.SaveChanges();

            var attachedRange = db.Orders.AsNoTracking().First();
            db.Orders.AttachRange(attachedRange);
            {|LC048:attachedRange.Quantity|}++;
            db.SaveChanges();
        }

        public void InvalidOverrideChainsStayQuiet(
            InvalidStateOverrideContext updateContext,
            InvalidStateOverrideContext attachContext,
            InvalidStateOverrideSetContext updateSetContext,
            InvalidStateOverrideSetContext attachSetContext)
        {
            updateContext.ChangeTracker.AutoDetectChangesEnabled = false;
            var contextUpdate = updateContext.Orders.First();
            contextUpdate.Quantity++;
            updateContext.Update(contextUpdate);
            updateContext.SaveChanges();

            var contextUpdateRange = updateContext.Orders.First();
            contextUpdateRange.Quantity++;
            updateContext.UpdateRange(contextUpdateRange);
            updateContext.SaveChanges();

            var contextAttach = attachContext.Orders.AsNoTracking().First();
            attachContext.Attach(contextAttach);
            contextAttach.Quantity++;
            attachContext.SaveChanges();

            var contextAttachRange = attachContext.Orders.AsNoTracking().First();
            attachContext.AttachRange(contextAttachRange);
            contextAttachRange.Quantity++;
            attachContext.SaveChanges();

            updateSetContext.ChangeTracker.AutoDetectChangesEnabled = false;
            var setUpdate = updateSetContext.Orders.First();
            setUpdate.Quantity++;
            updateSetContext.Orders.Update(setUpdate);
            updateSetContext.SaveChanges();

            var setUpdateRange = updateSetContext.Orders.First();
            setUpdateRange.Quantity++;
            updateSetContext.Orders.UpdateRange(setUpdateRange);
            updateSetContext.SaveChanges();

            var setAttach = attachSetContext.Orders.AsNoTracking().First();
            attachSetContext.Orders.Attach(setAttach);
            setAttach.Quantity++;
            attachSetContext.SaveChanges();

            var setAttachRange = attachSetContext.Orders.AsNoTracking().First();
            attachSetContext.Orders.AttachRange(setAttachRange);
            setAttachRange.Quantity++;
            attachSetContext.SaveChanges();
        }

        public void AttachDoesNotMarkModified(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.Attach(order);
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConditionalRemapDoesNotReactivateIgnoredTimestamp()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class VersionedOrder
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        [System.ComponentModel.DataAnnotations.Timestamp]
        public byte[] Version { get; set; }
    }

    public sealed class ConditionalContext : DbContext
    {
        public DbSet<VersionedOrder> Orders { get; set; }
        public bool Remap { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VersionedOrder>().Ignore(x => x.Version);
            if (Remap)
                modelBuilder.Entity<VersionedOrder>().Property(x => x.Version);
        }
    }

    public sealed class UnconditionalContext : DbContext
    {
        public DbSet<VersionedOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VersionedOrder>().Ignore(x => x.Version);
            modelBuilder.Entity<VersionedOrder>().Property(x => x.Version);
        }
    }

    public sealed class Service
    {
        public void Conditional(ConditionalContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Unconditional(UnconditionalContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task RemoveSuppressionRequiresEfDeclaredMethod()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class Order
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class RealContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class LookalikeContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new void Remove<T>(T entity) where T : class { }
        public new void RemoveRange(params object[] entities) { }
    }

    public sealed class DelegatingRemoveContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove<TEntity>(TEntity entity) => base.Remove(entity);
        public override void RemoveRange(params object[] entities) =>
            base.RemoveRange(entities);
    }

    public sealed class DelegatingRemoveDbSet<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove(TEntity entity) => base.Remove(entity);
        public override void RemoveRange(params TEntity[] entities) =>
            base.RemoveRange(entities);
    }

    public sealed class DelegatingRemoveSetContext : DbContext
    {
        public DelegatingRemoveDbSet<Order> Orders { get; set; }
    }

    public class HidingRemoveBaseContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove<TEntity>(TEntity entity) where TEntity : class =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void RemoveRange(params object[] entities) { }
    }

    public sealed class InvalidRemoveOverrideContext : HidingRemoveBaseContext
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove<TEntity>(TEntity entity) => base.Remove(entity);
        public override void RemoveRange(params object[] entities) =>
            base.RemoveRange(entities);
    }

    public class HidingRemoveDbSetBase<TEntity> : DbSet<TEntity>
        where TEntity : class
    {
        public new virtual Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove(TEntity entity) =>
            new Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>();
        public new virtual void RemoveRange(params TEntity[] entities) { }
    }

    public sealed class InvalidRemoveOverrideDbSet<TEntity> : HidingRemoveDbSetBase<TEntity>
        where TEntity : class
    {
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>
            Remove(TEntity entity) => base.Remove(entity);
        public override void RemoveRange(params TEntity[] entities) =>
            base.RemoveRange(entities);
    }

    public sealed class InvalidRemoveOverrideSetContext : DbContext
    {
        public InvalidRemoveOverrideDbSet<Order> Orders { get; set; }
    }


    public sealed class Service
    {
        public void RealContextRemove(RealContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Remove(order);
            db.SaveChanges();
        }

        public void RealSetRemove(RealContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.Orders.Remove(order);
            db.SaveChanges();
        }

        public void DelegatedOverridesSuppress(
            DelegatingRemoveContext context,
            DelegatingRemoveSetContext setContext)
        {
            var contextSingle = context.Orders.First();
            contextSingle.Quantity++;
            context.Remove(contextSingle);
            context.SaveChanges();

            var contextRange = context.Orders.First();
            contextRange.Quantity++;
            context.RemoveRange(contextRange);
            context.SaveChanges();

            var setSingle = setContext.Orders.First();
            setSingle.Quantity++;
            setContext.Orders.Remove(setSingle);
            setContext.SaveChanges();

            var setRange = setContext.Orders.First();
            setRange.Quantity++;
            setContext.Orders.RemoveRange(setRange);
            setContext.SaveChanges();
        }

        public void InvalidOverrideChainsDoNotSuppress(
            InvalidRemoveOverrideContext context,
            InvalidRemoveOverrideSetContext setContext)
        {
            var contextSingle = context.Orders.First();
            {|LC048:contextSingle.Quantity|}++;
            context.Remove(contextSingle);
            context.SaveChanges();

            var contextRange = context.Orders.First();
            {|LC048:contextRange.Quantity|}++;
            context.RemoveRange(contextRange);
            context.SaveChanges();

            var setSingle = setContext.Orders.First();
            {|LC048:setSingle.Quantity|}++;
            setContext.Orders.Remove(setSingle);
            setContext.SaveChanges();

            var setRange = setContext.Orders.First();
            {|LC048:setRange.Quantity|}++;
            setContext.Orders.RemoveRange(setRange);
            setContext.SaveChanges();
        }

        public void Lookalikes(LookalikeContext db)
        {
            var first = db.Orders.First();
            {|LC048:first.Quantity|}++;
            db.Remove(first);
            db.SaveChanges();

            var second = db.Orders.First();
            {|LC048:second.Quantity|}++;
            db.RemoveRange(second);
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task HelperTransactionsCorrelateContextPredicateOrderAndPath()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Unrelated(AppDbContext db, AppDbContext audit)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            Begin(audit);
            db.SaveChanges();
        }

        public void Matching(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            Begin(db);
            db.SaveChanges();
        }

        public void True(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            BeginMaybe(db, true);
            db.SaveChanges();
        }

        public void False(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            BeginMaybe(db, false);
            db.SaveChanges();
        }

        public void Unknown(AppDbContext db, bool useTransaction)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            BeginMaybe(db, useTransaction);
            db.SaveChanges();
        }

        public void Correlated(AppDbContext db, bool useTransaction)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            BeginMaybe(db, useTransaction);
            if (useTransaction)
                db.SaveChanges();
        }

        public void AfterSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
            Begin(db);
        }

        public void ContainedBeforeSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            BeginThenSave(db);
        }

        public void ContainedAfterSave(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            SaveThenBegin(db);
        }

        private static void Begin(AppDbContext db)
        {
            db.Database.BeginTransaction();
        }

        private static void BeginMaybe(AppDbContext db, bool useTransaction)
        {
            if (useTransaction)
                db.Database.BeginTransaction();
        }

        private static void BeginThenSave(AppDbContext db)
        {
            using var transaction = db.Database.BeginTransaction();
            db.SaveChanges();
        }

        private static void SaveThenBegin(AppDbContext db)
        {
            db.SaveChanges();
            db.Database.BeginTransaction();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ComputedAndHidingDbSetPropertiesAreNotStableOrigins()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class Order
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public class BaseContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class RoutedContext : BaseContext
    {
        private readonly BaseContext other;
        public DbSet<Order> StableOrders { get; set; }
        public DbSet<Order> ComputedOrders => other.Orders;
        public new DbSet<Order> Orders => other.Orders;
    }

    public sealed class Service
    {
        public void Stable(RoutedContext db)
        {
            var order = db.StableOrders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Computed(RoutedContext db)
        {
            var order = db.ComputedOrders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Hiding(RoutedContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConcurrentFluentCachePublishesCompleteModel()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class Order
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class ProtectedContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(x => x.Quantity).IsConcurrencyToken();
        }
    }

    public sealed class PlainContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class KeylessContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasNoKey();
        }
    }

    public sealed class Service
    {
        public void Protected(ProtectedContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Plain(PlainContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Keyless(KeylessContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CompletedMatchingReloadRestoresLoadedStateDependence()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Definite(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            order.Quantity++;
            db.SaveChanges();
        }

        public void Conditional(AppDbContext db, bool reset)
        {
            var order = db.Orders.First();
            if (reset)
                order.Quantity = 0;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ReloadAfterBlindInitialization(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            db.Entry(order).Reload();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public async Task AwaitedReloadAfterBlindInitialization(AppDbContext db)
        {
            var order = await db.Orders.FirstAsync();
            order.Quantity = 0;
            await db.Entry(order).ReloadAsync().ConfigureAwait(false);
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync();
        }

        public void UnrelatedEntityReload(AppDbContext db)
        {
            var order = db.Orders.First();
            var other = db.Orders.First();
            order.Quantity = 0;
            db.Entry(other).Reload();
            order.Quantity++;
            db.SaveChanges();
        }

        public void UnrelatedContextReload(AppDbContext db, AppDbContext otherDb)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            otherDb.Entry(order).Reload();
            order.Quantity++;
            db.SaveChanges();
        }

        public void UnobservedReloadAsync(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            _ = db.Entry(order).ReloadAsync();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ReloadOnDisjointPath(AppDbContext db, bool refresh)
        {
            var order = db.Orders.First();
            order.Quantity = 0;
            if (refresh)
            {
                db.Entry(order).Reload();
            }
            else
            {
                order.Quantity++;
                db.SaveChanges();
            }
        }
        public void ThrowingStraightLineInitialization(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = Compute();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ThrowingInitializationWithCompletingCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity = Compute();
            }
            catch (InvalidOperationException)
            {
            }

            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ThrowingInitializationWithTerminatingCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity = Compute();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            order.Quantity++;
            db.SaveChanges();
        }

        public void ThrowingInitializationWithFinallyMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity = Compute();
            }
            finally
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }

        private static int Compute() => throw new InvalidOperationException();

    }
}
"""
        );
    }

    [Fact]
    public async Task NonEffectiveFluentKeyConfigurationsDoNotSuppressDiagnostics()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public sealed class Order
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }

        private static void Unused(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasKey(x => x.Code);
            modelBuilder.Entity<Order>().HasAlternateKey(x => x.Code);
        }
    }

    public abstract class AlternateKeyNegativeContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class ConditionalAlternateKeyContext : AlternateKeyNegativeContext
    {
        private readonly bool enabled;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (enabled)
                modelBuilder.Entity<Order>().HasAlternateKey(x => x.Code);
        }
    }

    public sealed class DeadAlternateKeyContext : AlternateKeyNegativeContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (false)
                modelBuilder.Entity<Order>().HasAlternateKey(x => x.Code);
        }
    }

    public sealed class LookalikeAlternateKeyContext : AlternateKeyNegativeContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AlternateKeyLookalike.HasAlternateKey(
                modelBuilder.Entity<Order>(),
                x => x.Code);
        }
    }

    public sealed class UnboundAlternateKeyContext : AlternateKeyNegativeContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var unbound =
                new Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order>();
            unbound.HasAlternateKey(x => x.Code);
        }
    }

    public static class AlternateKeyLookalike
    {
        public static void HasAlternateKey<T>(
            Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> builder,
            System.Linq.Expressions.Expression<System.Func<T, object>> key)
            where T : class
        {
        }
    }

    public sealed class Service
    {
        public void Update(KeyContext db)
        {
            var order = db.Orders.First();
            order.Id++;
            {|LC048:order.Code|} += "x";
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Conditional(ConditionalAlternateKeyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Code|} += "x";
            db.SaveChanges();
        }

        public void Dead(DeadAlternateKeyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Code|} += "x";
            db.SaveChanges();
        }

        public void Lookalike(LookalikeAlternateKeyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Code|} += "x";
            db.SaveChanges();
        }

        public void Unbound(UnboundAlternateKeyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Code|} += "x";
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task FluentKeysRecognizeInheritedPropertiesForDerivedEntities()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public class Root
    {
        public int Id { get; set; }
        public string RootCode { get; set; }
    }

    public sealed class Derived : Root
    {
        public int Quantity { get; set; }
    }

    public sealed class DirectDerived : Root
    {
        public int Quantity { get; set; }
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<Derived> Deriveds { get; set; }
        public DbSet<DirectDerived> DirectDeriveds { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Root>().HasKey(x => x.RootCode);
            modelBuilder.Entity<DirectDerived>().HasKey(x => x.Id);
        }
    }

    public sealed class Service
    {
        public void RootConfiguration(KeyContext db)
        {
            var entity = db.Deriveds.First();
            {|LC048:entity.Id|}++;
            entity.RootCode += "x";
            {|LC048:entity.Quantity|}++;
            db.SaveChanges();
        }

        public void DerivedConfiguration(KeyContext db)
        {
            var entity = db.DirectDeriveds.First();
            entity.Id++;
            {|LC048:entity.RootCode|} += "x";
            {|LC048:entity.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task OperationCandidateGatePreservesRelevantAnalysis()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void WithoutMaterialization(Order order, AppDbContext db)
        {
            order.Quantity++;
            db.SaveChanges();
        }

        public void WithoutMutation(AppDbContext db)
        {
            var order = db.Orders.First();
            db.SaveChanges();
        }

        public void WithoutSave(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
        }

        public void Candidate(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task StableGuardsAndConditionalNoTrackingCorrelatePaths()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db, bool trackAll)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (trackAll)
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ComplementaryMutationAndSaveGuards(
            AppDbContext db,
            bool update)
        {
            var order = db.Orders.First();
            if (update)
                order.Quantity++;
            if (!update)
                db.SaveChanges();
        }

        public void CompatibleMutationAndSaveGuards(
            AppDbContext db,
            bool update)
        {
            var order = db.Orders.First();
            if (update)
                {|LC048:order.Quantity|}++;
            if (update)
                db.SaveChanges();
        }

        public void ReassignedGuardRemainsConservative(
            AppDbContext db,
            bool update)
        {
            var order = db.Orders.First();
            if (update)
                {|LC048:order.Quantity|}++;
            update = false;
            if (!update)
                db.SaveChanges();
        }

        public void GuardedMutationWithFallthroughSave(
            AppDbContext db,
            bool update)
        {
            var order = db.Orders.First();
            if (update)
                {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void SameConditionNoTrackingPath(
            AppDbContext db,
            bool noTracking)
        {
            if (noTracking)
                db.ChangeTracker.QueryTrackingBehavior =
                    QueryTrackingBehavior.NoTracking;
            var order = db.Orders.First();
            if (noTracking)
            {
                order.Quantity++;
                db.SaveChanges();
            }
        }

        public void ComplementaryTrackedPath(
            AppDbContext db,
            bool noTracking)
        {
            if (noTracking)
                db.ChangeTracker.QueryTrackingBehavior =
                    QueryTrackingBehavior.NoTracking;
            var order = db.Orders.First();
            if (!noTracking)
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }

        public void ConditionalNoTrackingWithOrdinaryFallthrough(
            AppDbContext db,
            bool noTracking)
        {
            if (noTracking)
                db.ChangeTracker.QueryTrackingBehavior =
                    QueryTrackingBehavior.NoTracking;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UnknownNoTrackingCondition(
            AppDbContext db,
            bool update)
        {
            if (DateTime.UtcNow.Ticks > 0)
                db.ChangeTracker.QueryTrackingBehavior =
                    QueryTrackingBehavior.NoTracking;
            var order = db.Orders.First();
            if (update)
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }
        public void TrackAllAndMutationSaveOnSameBranch(
            AppDbContext db,
            bool trackAll)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (trackAll)
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            var order = db.Orders.First();
            if (trackAll)
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
            }
        }

        public void TrackAllAndMutationSaveOnComplementaryBranch(
            AppDbContext db,
            bool trackAll)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (trackAll)
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            var order = db.Orders.First();
            if (!trackAll)
            {
                order.Quantity++;
                db.SaveChanges();
            }
        }

        public void UnknownTrackingAndMutationSaveOnComplementaryBranch(
            AppDbContext db,
            QueryTrackingBehavior behavior,
            bool reassign)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (reassign)
                db.ChangeTracker.QueryTrackingBehavior = behavior;

            var order = db.Orders.First();
            if (!reassign)
            {
                order.Quantity++;
                db.SaveChanges();
            }
        }

    }
}
"""
        );
    }

    [Fact]
    public async Task AutoDetectionPersistenceRequiresExactEfSymbols()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class CustomTracker
    {
        public void DetectChanges() { }
    }

    public sealed class HiddenTrackerContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public new CustomTracker ChangeTracker { get; } = new CustomTracker();
    }

    public sealed class Service
    {
        public void Conditional(AppDbContext db, bool enable)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            if (enable)
                db.ChangeTracker.AutoDetectChangesEnabled = true;
            db.SaveChanges();
        }

        public void Nonconstant(AppDbContext db, bool enabled)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.ChangeTracker.AutoDetectChangesEnabled = enabled;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void OtherContext(AppDbContext db, AppDbContext other, bool enabled)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            other.ChangeTracker.AutoDetectChangesEnabled = enabled;
            db.SaveChanges();
        }

        public void GenuineDetectChangesPersistsMutation(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }

        public void HiddenTrackerDoesNotProvePersistence(HiddenTrackerContext db)
        {
            ((DbContext)db).ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task PersistenceMarksBeforeMutationRequireMatchingProof()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            db.Update(order);
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void UpdateRange(AppDbContext db, bool persist)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            if (persist)
                db.UpdateRange(order);
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void State(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            db.Entry(order).State = EntityState.Modified;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Property(AppDbContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            db.Entry(order).Property(x => x.Quantity).IsModified = true;
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Mismatches(AppDbContext db, AppDbContext other)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            var otherOrder = db.Orders.First();
            other.Update(order);
            db.Update(otherOrder);
            db.Entry(order).Property(x => x.Status).IsModified = true;
            order.Quantity++;
            db.SaveChanges();
        }

        public void Nonreaching(AppDbContext db, bool persist)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            if (persist)
            {
                db.Update(order);
                return;
            }

            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task InheritedConventionalTypeNameKeyStaysQuiet()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public class AggregateRoot
    {
        public int AggregateRootId { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class Purchase : AggregateRoot
    {
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<Purchase> Purchases { get; set; }
    }

    public sealed class Service
    {
        public void Update(KeyContext db)
        {
            var purchase = db.Purchases.First();
            purchase.AggregateRootId++;
            {|LC048:purchase.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task NonconstantTrackingBehaviorReassignmentInvalidatesDefiniteNoTracking()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void Nonconstant(
            AppDbContext db,
            QueryTrackingBehavior behavior)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.QueryTrackingBehavior = behavior;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Conditional(
            AppDbContext db,
            QueryTrackingBehavior behavior,
            bool reassign)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            if (reassign)
                db.ChangeTracker.QueryTrackingBehavior = behavior;

            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void OtherContext(
            AppDbContext db,
            AppDbContext other,
            QueryTrackingBehavior behavior)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            other.ChangeTracker.QueryTrackingBehavior = behavior;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void StillNoTracking(AppDbContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.QueryTrackingBehavior =
                QueryTrackingBehavior.NoTrackingWithIdentityResolution;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task TypeLevelPrimaryKeyAttributeSupportsCompositeInheritedKeys()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    [PrimaryKey(nameof(TenantId), nameof(Code))]
    public class AggregateRoot
    {
        public int TenantId { get; set; }
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class Purchase : AggregateRoot
    {
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<Purchase> Purchases { get; set; }
    }

    public sealed class Service
    {
        public void Update(KeyContext db)
        {
            var purchase = db.Purchases.First();
            purchase.TenantId++;
            purchase.Code += "x";
            {|LC048:purchase.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task StringFluentKeysResolveAgainstEntityHierarchy()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public class KeyRoot
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string Code { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class NameofOrder : KeyRoot
    {
    }

    public sealed class LiteralOrder : KeyRoot
    {
    }

    public sealed class CompositeOrder : KeyRoot
    {
    }

    public sealed class KeyContext : DbContext
    {
        public DbSet<NameofOrder> NameofOrders { get; set; }
        public DbSet<LiteralOrder> LiteralOrders { get; set; }
        public DbSet<CompositeOrder> CompositeOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NameofOrder>().HasKey(nameof(NameofOrder.Code));
            modelBuilder.Entity<LiteralOrder>().HasKey("Code");
            modelBuilder.Entity<CompositeOrder>().HasKey("TenantId", "Code");
        }
    }

    public sealed class Service
    {
        public void Nameof(KeyContext db)
        {
            var order = db.NameofOrders.First();
            order.Code += "x";
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Literal(KeyContext db)
        {
            var order = db.LiteralOrders.First();
            order.Code += "x";
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Composite(KeyContext db)
        {
            var order = db.CompositeOrders.First();
            order.TenantId++;
            order.Code += "x";
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task StringConcurrencyPropertiesResolveAgainstEntityHierarchy()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;

    public class ConcurrencyRoot
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class NameofOrder : ConcurrencyRoot
    {
    }

    public sealed class LiteralOrder : ConcurrencyRoot
    {
    }

    public sealed class ShadowOrder : ConcurrencyRoot
    {
    }

    public sealed class ConcurrencyContext : DbContext
    {
        public DbSet<NameofOrder> NameofOrders { get; set; }
        public DbSet<LiteralOrder> LiteralOrders { get; set; }
        public DbSet<ShadowOrder> ShadowOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<NameofOrder>()
                .Property<int>(nameof(NameofOrder.Quantity))
                .IsConcurrencyToken();
            modelBuilder
                .Entity<LiteralOrder>()
                .Property<int>("Quantity")
                .IsConcurrencyToken();
            modelBuilder
                .Entity<ShadowOrder>()
                .Property<int>("ShadowQuantity")
                .IsConcurrencyToken();
        }
    }

    public sealed class Service
    {
        public void Nameof(ConcurrencyContext db)
        {
            var order = db.NameofOrders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Literal(ConcurrencyContext db)
        {
            var order = db.LiteralOrders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ShadowLookalike(ConcurrencyContext db)
        {
            var order = db.ShadowOrders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task SaveEvidenceRequiresEfSignaturesOrOverrides()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class StandardSaveContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class OverrideSaveContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public override int SaveChanges(bool acceptAllChangesOnSuccess) => 0;

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    public sealed class LookalikeSaveContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        public new int SaveChanges(bool acceptAllChangesOnSuccess) => 0;

        public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    public sealed class Service
    {
        public void StandardSync(StandardSaveContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges(false);
        }

        public async Task StandardAsync(StandardSaveContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync(false, CancellationToken.None);
        }

        public void OverrideSync(OverrideSaveContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges(false);
        }

        public async Task OverrideAsync(OverrideSaveContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            await db.SaveChangesAsync(false, CancellationToken.None);
        }

        public void LookalikeSync(LookalikeSaveContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges(false);
        }

        public async Task LookalikeAsync(LookalikeSaveContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            await db.SaveChangesAsync();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task LoopCarriedTrackingBehaviorReassignmentsAffectLaterMaterializations()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void TrackAllOnBackedge(
            AppDbContext db,
            bool repeat)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            while (repeat)
            {
                var order = db.Orders.First();
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            }
        }

        public void UnknownOnBackedge(
            AppDbContext db,
            bool repeat,
            QueryTrackingBehavior behavior)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            while (repeat)
            {
                var order = db.Orders.First();
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
                db.ChangeTracker.QueryTrackingBehavior = behavior;
            }
        }

        public void ResetOnEveryIteration(AppDbContext db, bool repeat)
        {
            while (repeat)
            {
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                var order = db.Orders.First();
                order.Quantity++;
                db.SaveChanges();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            }
        }

        public void ReassignmentAfterLoop(AppDbContext db, bool repeat)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            while (repeat)
            {
                var order = db.Orders.First();
                order.Quantity++;
                db.SaveChanges();
            }

            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task LoopCarriedAutoDetectionReenablementAffectsLaterSaves()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void EnabledOnBackedge(AppDbContext db, bool repeat)
        {
            var order = db.Orders.First();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            while (repeat)
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
                db.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        }

        public void UnknownOnBackedge(AppDbContext db, bool repeat, bool enabled)
        {
            var order = db.Orders.First();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            while (repeat)
            {
                {|LC048:order.Quantity|}++;
                db.SaveChanges();
                db.ChangeTracker.AutoDetectChangesEnabled = enabled;
            }
        }

        public void ResetOnEveryIteration(AppDbContext db, bool repeat)
        {
            var order = db.Orders.First();
            while (repeat)
            {
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                order.Quantity++;
                db.SaveChanges();
                db.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        }

        public void ReenableAfterLoop(AppDbContext db, bool repeat)
        {
            var order = db.Orders.First();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            while (repeat)
            {
                order.Quantity++;
                db.SaveChanges();
            }

            db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task CatchSaveRecognizesReachableImplicitExceptionsOnly()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void PotentialInvocation(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                {|LC048:order.Quantity|}++;
                db.Orders.First();
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void KnownNonthrowingOperations(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
                var copy = 1;
                copy += 2;
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void ExclusiveBranches(AppDbContext db, bool mutate)
        {
            var order = db.Orders.First();
            try
            {
                if (mutate)
                {
                    order.Quantity++;
                    return;
                }

                db.Orders.First();
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void NestedExecutable(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
                Func<Order> deferred = () => db.Orders.First();
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void FilteredCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
                db.Orders.First();
            }
            catch (Exception) when (DateTime.UtcNow.Ticks > 0)
            {
                db.SaveChanges();
            }
        }

        public void UnrelatedCatch(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                order.Quantity++;
            }
            catch (Exception)
            {
            }

            try
            {
                db.Orders.First();
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedExplicitThrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    throw new InvalidOperationException();
                }
                finally
                {
                }
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedImplicitThrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    db.Orders.First();
                }
                finally
                {
                }
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void InnerCatchInterceptsNestedThrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    order.Quantity++;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesFilteredNestedExplicitThrow(
            AppDbContext db,
            bool handle)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (handle)
                {
                }
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesFilteredNestedImplicitThrow(
            AppDbContext db,
            bool handle)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    db.Orders.First();
                }
                catch (Exception) when (handle)
                {
                }
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedExplicitRethrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedImplicitRethrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    db.Orders.First();
                }
                catch (Exception)
                {
                    throw;
                }
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedNamedRethrow(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException error)
                {
                    throw error;
                }
            }
            catch (InvalidOperationException)
            {
                db.SaveChanges();
            }
        }

        public void OuterCatchHandlesNestedReplacementException(AppDbContext db)
        {
            var order = db.Orders.First();
            try
            {
                try
                {
                    {|LC048:order.Quantity|}++;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    throw new ArgumentException();
                }
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConfiguredDefaultNoTrackingIsContextScopedAndConservative()
    {
        await VerifyAsync(
            """
namespace Test
{
    using System;
    using System.Linq;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;

    public sealed class Order
    {
        public int Quantity { get; set; }
    }

    public class NoTrackingContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    public sealed class InheritedNoTrackingContext : NoTrackingContext
    {
    }

    public sealed class ReplacedNoTrackingContext : NoTrackingContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) { }
    }

    public sealed class TrackingAfterBaseContext : NoTrackingContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            base.OnConfiguring(options);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        }
    }

    public sealed class TrackingBeforeBaseContext : NoTrackingContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
            base.OnConfiguring(options);
        }
    }

    public sealed class RegisteredContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public interface IRegisteredContextService { }

    public sealed class RegisteredImplementationContext : DbContext, IRegisteredContextService
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class PooledContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class PooledImplementationContext : DbContext, IRegisteredContextService
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class FactoryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class FactoryImplementationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class PooledFactoryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class AmbiguousRegistrationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class LookalikeRegistrationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class CustomContextFactory { }

    public static class LookalikeRegistrations
    {
        public static IServiceCollection AddDbContextFactory<TContext>(
            IServiceCollection services,
            Action<DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext => services;
    }

    public sealed class OrdinaryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class ConditionalContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (DateTime.UtcNow.Ticks > 0)
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }
    }

    public sealed class AmbiguousContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        private QueryTrackingBehavior Behavior => QueryTrackingBehavior.NoTracking;
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseQueryTrackingBehavior(Behavior);
    }

    public sealed class LookalikeContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            Lookalike.UseQueryTrackingBehavior(options, QueryTrackingBehavior.NoTracking);
    }

    public static class Lookalike
    {
        public static void UseQueryTrackingBehavior(
            DbContextOptionsBuilder options,
            QueryTrackingBehavior behavior) { }
    }

    public sealed class Startup
    {
        public void Configure(IServiceCollection services)
        {
            services.AddDbContext<RegisteredContext>(
                options => options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContext<IRegisteredContextService, RegisteredImplementationContext>(
                options => options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContextPool<PooledContext>(
                (provider, options) =>
                    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContextPool<IRegisteredContextService, PooledImplementationContext>(
                options => options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContextFactory<FactoryContext>(
                options => options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContextFactory<FactoryImplementationContext, CustomContextFactory>(
                (provider, options) =>
                    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddPooledDbContextFactory<PooledFactoryContext>(
                (provider, options) =>
                    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddDbContext<AmbiguousRegistrationContext>(
                (first, second) =>
                    first.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            LookalikeRegistrations.AddDbContextFactory<LookalikeRegistrationContext>(
                services,
                options => options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        }
    }

    public sealed class Service
    {
        public void DefaultNoTracking(NoTrackingContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void EffectiveTrackAll(NoTrackingContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EffectiveUnknown(
            NoTrackingContext db,
            QueryTrackingBehavior behavior)
        {
            db.ChangeTracker.QueryTrackingBehavior = behavior;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void PossibleUnknown(
            NoTrackingContext db,
            QueryTrackingBehavior behavior,
            bool reassign)
        {
            if (reassign)
                db.ChangeTracker.QueryTrackingBehavior = behavior;

            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ProvenNoTracking(NoTrackingContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.QueryTrackingBehavior =
                QueryTrackingBehavior.NoTrackingWithIdentityResolution;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitNoTracking(NoTrackingContext db)
        {
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            var order = db.Orders.AsNoTracking().First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void InheritedDefault(InheritedNoTrackingContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ReplacedDefault(ReplacedNoTrackingContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void TrackingAfterBase(TrackingAfterBaseContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void TrackingBeforeBase(TrackingBeforeBaseContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ExplicitTracking(NoTrackingContext db)
        {
            var order = db.Orders.AsTracking().First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void RegisteredDefault(RegisteredContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ActualContextScope(OrdinaryContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalConfiguration(ConditionalContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void AmbiguousConfiguration(AmbiguousContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeConfiguration(LookalikeContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void RegisteredImplementationDefault(RegisteredImplementationContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void PooledDefault(PooledContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void PooledImplementationDefault(PooledImplementationContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void FactoryDefault(FactoryContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void FactoryImplementationDefault(FactoryImplementationContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void PooledFactoryDefault(PooledFactoryContext db)
        {
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void AmbiguousRegistration(AmbiguousRegistrationContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeRegistration(LookalikeRegistrationContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task IndependentNotificationTrackingKeepsAutoDetectionDiagnostics()
    {
        await VerifyAsync(
            """
namespace Test
{
    using System;
    using System.Linq;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;

    public class Order
    {
        public int Quantity { get; set; }
    }

    public sealed class OtherOrder
    {
        public int Quantity { get; set; }
    }

    public sealed class DerivedOrder : Order
    {
    }

    public sealed class LookalikeEntityTypeBuilder<TEntity>
    {
        public LookalikeEntityTypeBuilder<TEntity> HasChangeTrackingStrategy(
            ChangeTrackingStrategy strategy) => this;
    }

    public class NotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasChangeTrackingStrategy(
                ChangeTrackingStrategy.ChangingAndChangedNotifications);
    }

    public sealed class ChangedNotificationsContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasChangeTrackingStrategy(
                ChangeTrackingStrategy.ChangedNotifications);
    }

    public sealed class EntityNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OtherOrder> OtherOrders { get; set; }
        public DbSet<DerivedOrder> DerivedOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder
                .Entity<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
    }

    public sealed class CallbackEntityNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(builder =>
                builder.HasChangeTrackingStrategy(
                    ChangeTrackingStrategy.ChangingAndChangedNotifications));
    }

    public sealed class GlobalThenEntitySnapshotContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OtherOrder> OtherOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
            modelBuilder
                .Entity<OtherOrder>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
        }
    }

    public sealed class EntityThenGlobalSnapshotContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
        }
    }

    public class EntityNotificationBaseContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder
                .Entity<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
    }

    public sealed class InheritedEntityNotificationContext : EntityNotificationBaseContext
    {
    }

    public sealed class EntitySnapshotAfterBaseContext : EntityNotificationBaseContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
        }
    }

    public sealed class EntitySnapshotBeforeBaseContext : EntityNotificationBaseContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class ConditionalEntityNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.UtcNow.Ticks > 0)
            {
                modelBuilder
                    .Entity<Order>()
                    .HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
            }
        }
    }

    public sealed class GuaranteedNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (true)
                modelBuilder.HasChangeTrackingStrategy(
                    ChangeTrackingStrategy.ChangedNotifications);
        }
    }

    public sealed class GuaranteedEntityNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(builder =>
            {
                if (true)
                    builder.HasChangeTrackingStrategy(
                        ChangeTrackingStrategy.ChangedNotifications);
            });
        }
    }

    public sealed class LookalikeEntityNotificationContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            new LookalikeEntityTypeBuilder<Order>()
                .HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);
    }

    public sealed class InheritedNotificationContext : NotificationContext
    {
    }

    public sealed class ReplacedNotificationContext : NotificationContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public sealed class NotificationAfterBaseContext : NotificationContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
        }
    }

    public sealed class NotificationBeforeBaseContext : NotificationContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
            base.OnModelCreating(modelBuilder);
        }
    }

    public sealed class OriginalValuesContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasChangeTrackingStrategy(
                ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
    }

    public class ProxyContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseChangeTrackingProxies();
    }

    public sealed class InheritedProxyContext : ProxyContext
    {
    }

    public sealed class ReplacedProxyContext : ProxyContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) { }
    }

    public sealed class ProxyAfterBaseContext : ProxyContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            base.OnConfiguring(options);
            options.UseChangeTrackingProxies(false);
        }
    }

    public sealed class ProxyBeforeBaseContext : ProxyContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseChangeTrackingProxies(false);
            base.OnConfiguring(options);
        }
    }

    public sealed class RegisteredProxyContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class SnapshotContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
    }

    public sealed class ConditionalContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DateTime.UtcNow.Ticks > 0)
                modelBuilder.HasChangeTrackingStrategy(
                    ChangeTrackingStrategy.ChangingAndChangedNotifications);
        }
    }

    public sealed class OrdinaryContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class Startup
    {
        public void Configure(IServiceCollection services) =>
            services.AddDbContext<RegisteredProxyContext>(
                options => options.UseChangeTrackingProxies());
    }

    public sealed class Service
    {
        public void Notifications(NotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ChangedNotifications(ChangedNotificationsContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EntityNotifications(EntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EntityNotificationsDoNotLeak(EntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.OtherOrders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void EntityNotificationsApplyToDerivedEntities(EntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.DerivedOrders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void CallbackEntityNotifications(CallbackEntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EntitySnapshotOverridesGlobal(GlobalThenEntitySnapshotContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.OtherOrders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GlobalStillAppliesToOtherEntity(GlobalThenEntitySnapshotContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LaterGlobalSnapshotWins(EntityThenGlobalSnapshotContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void InheritedEntityNotifications(InheritedEntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void EntitySnapshotAfterBase(EntitySnapshotAfterBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void EntitySnapshotBeforeBase(EntitySnapshotBeforeBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ConditionalEntityNotifications(ConditionalEntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void GuaranteedNotifications(GuaranteedNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void GuaranteedEntityNotifications(GuaranteedEntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void LookalikeEntityNotifications(LookalikeEntityNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NotificationsWithOriginalValues(OriginalValuesContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Proxies(ProxyContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void InheritedProxy(InheritedProxyContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ReplacedProxy(ReplacedProxyContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ProxyAfterBase(ProxyAfterBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void ProxyBeforeBase(ProxyBeforeBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void InheritedNotification(InheritedNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void ReplacedNotification(ReplacedNotificationContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NotificationAfterBase(NotificationAfterBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NotificationBeforeBase(NotificationBeforeBaseContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void RegisteredProxies(RegisteredProxyContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }

        public void Snapshot(SnapshotContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void Conditional(ConditionalContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }

        public void NotificationSetterPersistsSelfAssignment(NotificationContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = order.Quantity;
            db.SaveChanges();
        }

        public void ProxySetterPersistsSelfAssignment(ProxyContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = order.Quantity;
            db.SaveChanges();
        }

        public void ActualContextScope(OrdinaryContext db)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var order = db.Orders.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    [Fact]
    public async Task ConstantShortCircuitOperandsIgnoreOnlyDeadPropertyReads()
    {
        await VerifyAsync(
            Domain
                + """
    public sealed class Service
    {
        public void DeadAnd(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = (false && order.Quantity > 0) ? 1 : 2;
            db.SaveChanges();
        }

        public void DeadOr(AppDbContext db)
        {
            var order = db.Orders.First();
            order.Quantity = (true || order.Quantity > 0) ? 1 : 2;
            db.SaveChanges();
        }

        public void LiveAnd(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = (true && order.Quantity > 0) ? 1 : 2;
            db.SaveChanges();
        }

        public void LiveOr(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = (false || order.Quantity > 0) ? 1 : 2;
            db.SaveChanges();
        }

        public void LiveLeftAnd(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = (order.Quantity > 0 && false) ? 1 : 2;
            db.SaveChanges();
        }

        public void LiveLeftOr(AppDbContext db)
        {
            var order = db.Orders.First();
            {|LC048:order.Quantity|} = (order.Quantity > 0 || true) ? 1 : 2;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    private static async Task VerifyAsync(
        string source,
        params DiagnosticResult[] expectedDiagnostics
    )
    {
        var test = new CSharpAnalyzerTest<LostUpdateRiskAnalyzer, XUnitVerifier>
        {
            TestCode = EfCoreMock + source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            (
                "/.globalconfig",
                """
is_global = true
dotnet_diagnostic.LC048.severity = warning
"""
            )
        );
        test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
        await test.RunAsync();
    }
}
