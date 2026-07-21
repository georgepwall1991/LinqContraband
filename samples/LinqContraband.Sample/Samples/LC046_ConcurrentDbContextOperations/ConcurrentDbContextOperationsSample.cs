using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC046_ConcurrentDbContextOperations;

public static class ConcurrentDbContextOperationsSample
{
    public static async Task RunAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Testing LC046...");

        await db.Users.AsNoTracking().AnyAsync(cancellationToken);
        await db.Users.AsNoTracking().AnyAsync(cancellationToken);
    }

    public static async Task ViolationAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var first = db.Users.AsNoTracking().AnyAsync(cancellationToken);
        var second = db.Users.AsNoTracking().AnyAsync(cancellationToken);
        await Task.WhenAll(first, second);
    }
}
