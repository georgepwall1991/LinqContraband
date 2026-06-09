using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC045_MissingInclude;

public class MissingIncludeSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC045...");

        // VIOLATION: ShippingAddress is never Included. With lazy-loading proxies every
        // iteration fires an extra query (read-side N+1); without proxies the navigation
        // is silently null and this throws.
        var customers = db.Customers.ToList();
        foreach (var c in customers)
        {
            Console.WriteLine(c.ShippingAddress.Street);
        }

        // CORRECT: eagerly load the navigation the loop reads.
        var loaded = db.Customers.Include(c => c.ShippingAddress).ToList();
        foreach (var c in loaded)
        {
            Console.WriteLine(c.ShippingAddress.Street);
        }

        // CORRECT: project exactly the data the loop needs — no entity, no Include.
        var streets = db.Customers.Select(c => c.ShippingAddress.Street).ToList();
        foreach (var street in streets)
        {
            Console.WriteLine(street);
        }
    }
}
