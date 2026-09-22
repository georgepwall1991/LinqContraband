using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC050_OrderByBeforeDistinct;

public static class OrderByBeforeDistinctSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC050...");

        // VIOLATION: SQL DISTINCT drops the ORDER BY, so these are 50 arbitrary names in no particular order.
        var names = db.Customers
            .TagWith("Customer names")
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .Distinct()
            .Take(50)
            .ToList();

        // CORRECT: sort after Distinct().
        var sortedNames = db.Customers
            .TagWith("Customer names")
            .Select(c => c.Name)
            .Distinct()
            .OrderBy(name => name)
            .Take(50)
            .ToList();

        Console.WriteLine(names.Count + sortedNames.Count);
    }
}
