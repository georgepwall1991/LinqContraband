using System.Transactions;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC058_TransactionScopeWithoutAsyncFlow;

// Not called from Program: the in-memory provider does not take part in ambient transactions.
public static class TransactionScopeWithoutAsyncFlowSample
{
    public static async Task AddUserAsync(AppDbContext db, string name, CancellationToken cancellationToken)
    {
        // VIOLATION: without async flow the ambient transaction stays on this thread. SaveChangesAsync
        // can resume on another one, where EF Core no longer sees the transaction, and disposing the
        // scope there throws.
        using var scope = new TransactionScope();
        db.Users.Add(new User { Id = Guid.NewGuid(), Name = name, Age = 30 });
        await db.SaveChangesAsync(cancellationToken);
        scope.Complete();
    }

    public static async Task AddUserWithAsyncFlowAsync(AppDbContext db, string name, CancellationToken cancellationToken)
    {
        // CORRECT: the ambient transaction flows across awaits.
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        db.Users.Add(new User { Id = Guid.NewGuid(), Name = name, Age = 30 });
        await db.SaveChangesAsync(cancellationToken);
        scope.Complete();
    }
}
