using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC021...");

        // VIOLATION: Bypasses global filters (e.g., multi-tenancy or soft-delete)
        var allUsers = db.Users.IgnoreQueryFilters().ToList();

        // CORRECT: Normal query respecting global filters
        var activeUsers = db.Users.ToList();
    }
}
