using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC026_MissingCancellationToken;

public class MissingCancellationTokenSample
{
    public static async Task RunAsync(AppDbContext db, CancellationToken ct)
    {
        Console.WriteLine("Testing LC026...");

        // VIOLATION: CancellationToken is available but not passed
        var users1 = await db.Users.ToListAsync();

        // VIOLATION: SaveChangesAsync without token
        await db.SaveChangesAsync();

        // CORRECT: Token passed correctly
        var users2 = await db.Users.ToListAsync(ct);
        await db.SaveChangesAsync(ct);
    }
}
