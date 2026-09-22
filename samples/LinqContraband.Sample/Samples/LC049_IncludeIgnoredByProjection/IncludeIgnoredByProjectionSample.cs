using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC049_IncludeIgnoredByProjection;

public static class IncludeIgnoredByProjectionSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC049...");

        // VIOLATION: the projection never returns Customer entities, so EF Core ignores the Include.
        var streets = db.Customers
            .TagWith("Customer streets")
            .Include(c => c.ShippingAddress)
            .OrderBy(c => c.Id)
            .Take(50)
            .Select(c => new { c.Name, c.ShippingAddress.Street })
            .ToList();

        // CORRECT: the projection already reads the related column; no Include needed.
        var sameStreets = db.Customers
            .TagWith("Customer streets")
            .OrderBy(c => c.Id)
            .Take(50)
            .Select(c => new { c.Name, c.ShippingAddress.Street })
            .ToList();

        Console.WriteLine(streets.Count + sameStreets.Count);
    }
}
