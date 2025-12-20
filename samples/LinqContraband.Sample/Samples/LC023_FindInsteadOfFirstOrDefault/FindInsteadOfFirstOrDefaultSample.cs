using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC023_FindInsteadOfFirstOrDefault;

public class FindInsteadOfFirstOrDefaultSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC023...");

        var userId = Guid.NewGuid();

        // VIOLATION: Always hits the database
        var user1 = db.Users.FirstOrDefault(u => u.Id == userId);

        // VIOLATION: SingleOrDefault variant
        var user2 = db.Users.SingleOrDefault(u => u.Id == userId);

        // CORRECT: Checks local cache (change tracker) first
        var user3 = db.Users.Find(userId);
    }
}
