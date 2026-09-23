using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC054_MigrateInsideTransaction;

// Not called from Program: the sample context uses the in-memory provider, which cannot run migrations.
public static class MigrateInsideTransactionSample
{
    public static async Task DemonstrateViolationAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // VIOLATION: the pre-EF Core 9 resilient-migration pattern. EF Core 9+ throws because
        // MigrateAsync starts inside a transaction the code already opened on the same context.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public static async Task DemonstrateFixAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // CORRECT: EF Core manages the migration transaction, lock, and retries itself.
        await db.Database.MigrateAsync(cancellationToken);
    }
}
