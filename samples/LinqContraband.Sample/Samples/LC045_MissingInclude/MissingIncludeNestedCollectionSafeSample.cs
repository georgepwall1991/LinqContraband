using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC045_MissingInclude;

public static class MissingIncludeNestedCollectionSafeSample
{
    public static void Run(NestedCollectionContext db)
    {
        var orders = db
            .Orders.AsNoTracking()
            .OrderBy(order => order.Id)
            .Take(50)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .TagWith("LC045 nested collection sample")
            .ToList();

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
}
