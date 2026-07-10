using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC045_MissingInclude;

public class MissingIncludeSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC045...");

        // VIOLATION: ShippingAddress is not Included. With lazy-loading proxies each
        // iteration can fire an extra query (read-side N+1); without another loading
        // mechanism or relationship fix-up, the navigation can remain null and throw.
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

    public static void RunNested(NestedCollectionContext db)
    {
        var orders = db
            .Orders.AsNoTracking()
            .OrderBy(order => order.Id)
            .Take(50)
            .TagWith("LC045 nested collection sample")
            .ToList();

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                // VIOLATION: neither Items nor Product was eagerly loaded.
                Console.WriteLine(item.Product.Name);
            }
        }
    }
}

public sealed class NestedCollectionContext : DbContext
{
    public DbSet<NestedOrder> Orders { get; set; } = null!;
    public DbSet<NestedItem> Items { get; set; } = null!;
    public DbSet<NestedProduct> Products { get; set; } = null!;
}

public sealed class NestedOrder
{
    public int Id { get; set; }
    public List<NestedItem> Items { get; set; } = [];
}

public sealed class NestedItem
{
    public int Id { get; set; }
    public int NestedOrderId { get; set; }
    public int NestedProductId { get; set; }
    public NestedProduct Product { get; set; } = null!;
}

public sealed class NestedProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
