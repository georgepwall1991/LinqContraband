using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC038_ExcessiveEagerLoading;

public static class ExcessiveEagerLoadingSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC038...");

        // ADVISORY: This eager-loading chain is deep enough to cross the default threshold.
        var customers = db.Customers
            .Include(customer => customer.ShippingAddress)
            .ThenInclude(address => address.ShippingCountry)
            .ThenInclude(country => country.ShippingRegion)
            .ThenInclude(region => region.Continent)
            .ToList();

        _ = customers;
    }
}
