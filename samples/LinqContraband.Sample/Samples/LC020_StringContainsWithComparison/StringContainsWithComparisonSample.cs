using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC020_StringContainsWithComparison;

public class StringContainsWithComparisonSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC020...");

        // VIOLATION: StringComparison overloads are not SQL-translatable in EF queries.
        var users1 = db.Users.AsNoTracking()
            .Where(u => u.Name.Contains("admin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Id)
            .Take(10)
            .ToList();

        // VIOLATION: StartsWith with StringComparison has the same issue.
        var users2 = db.Users.AsNoTracking()
            .Where(u => u.Name.StartsWith("A", StringComparison.CurrentCulture))
            .OrderBy(u => u.Id)
            .Take(10)
            .ToList();

        // CORRECT: Simple overload that translates to SQL LIKE
        var users3 = db.Users.AsNoTracking().Where(u => u.Name.Contains("admin")).OrderBy(u => u.Id).Take(10).ToList();
    }
}
