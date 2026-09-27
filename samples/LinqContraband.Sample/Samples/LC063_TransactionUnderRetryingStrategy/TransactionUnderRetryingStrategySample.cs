using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LinqContraband.Sample.Samples.LC063_TransactionUnderRetryingStrategy;

// Not called from Program: the sample does not run against a database that needs retries.
public static class TransactionUnderRetryingStrategySample
{
    public static async Task PlaceOrderAsync(ResilientOrdersDbContext db, CancellationToken cancellationToken)
    {
        // VIOLATION: the context retries on failure, and a retrying strategy cannot retry part of a
        // transaction. SaveChangesAsync inside this transaction throws InvalidOperationException.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Orders.Add(new ResilientOrder { Quantity = 1 });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task PlaceOrderThroughStrategyAsync(ResilientOrdersDbContext db, CancellationToken cancellationToken)
    {
        // CORRECT: the whole transaction runs through the execution strategy, which retries it as one unit.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Orders.Add(new ResilientOrder { Quantity = 1 });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}

public sealed class ResilientOrder
{
    public int Id { get; set; }
    public int Quantity { get; set; }
}

public sealed class ResilientOrdersDbContext : DbContext
{
    public DbSet<ResilientOrder> Orders { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // SQLite has no EnableRetryOnFailure(); the SQL Server, PostgreSQL and MySQL providers turn on the
        // same behavior with sqlOptions.EnableRetryOnFailure().
        optionsBuilder.UseSqlite(
            "Data Source=lc063.db",
            sqlite => sqlite.ExecutionStrategy(dependencies => new SampleRetryingExecutionStrategy(dependencies)));
    }
}

public sealed class SampleRetryingExecutionStrategy : ExecutionStrategy
{
    public SampleRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5))
    {
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
}
