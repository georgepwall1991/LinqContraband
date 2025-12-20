using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC020_StringContainsWithComparison;

public class StringContainsWithComparisonSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC020...");

        // VIOLATION: Likely to trigger client-side evaluation
        var users1 = db.Users.Where(u => u.Name.Contains("admin", StringComparison.OrdinalIgnoreCase)).ToList();

        // VIOLATION: StartsWith with StringComparison
        var users2 = db.Users.Where(u => u.Name.StartsWith("A", StringComparison.CurrentCulture)).ToList();

        // CORRECT: Simple overload that translates to SQL LIKE
        var users3 = db.Users.Where(u => u.Name.Contains("admin")).ToList();
    }
}
