using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.Corpus;

// Query per element, inside a lambda that an in-memory operator runs once per element.

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q20_SelectLambdaFirst
{
    public static List<Customer> Run(ShopDb db)
    {
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        return ids.Select(id => db.Customers.First(c => c.Id == id)).ToList();
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q21_ListForEachLambda
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        var customers = db.Customers.ToList();
        customers.ForEach(customer => total += db.Orders.Count(o => o.CustomerId == customer.Id));
        return total;
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q22_InMemoryWhereWithQueryPredicate
{
    public static List<Customer> Run(ShopDb db)
    {
        var customers = db.Customers.ToList();
        return customers.Where(customer => db.Orders.Any(o => o.CustomerId == customer.Id)).ToList();
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q23_ToDictionaryValueQuery
{
    public static Dictionary<int, int> Run(ShopDb db)
    {
        var customers = db.Customers.AsNoTracking().ToList();
        return customers.ToDictionary(c => c.Id, c => db.Orders.Count(o => o.CustomerId == c.Id));
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q24_WhenAllOverSelect
{
    public static async Task<int> Run(ShopDb db)
    {
        var ids = await db.Customers.Select(c => c.Id).ToListAsync();
        var counts = await Task.WhenAll(ids.Select(id => db.Orders.CountAsync(o => o.CustomerId == id)));
        return counts.Sum();
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q25_ParallelForEach
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        var ids = db.Customers.Select(c => c.Id).ToList();
        var oneAtATime = new ParallelOptions { MaxDegreeOfParallelism = 1 };
        Parallel.ForEach(ids, oneAtATime, id => total += db.Orders.Count(o => o.CustomerId == id));
        return total;
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q26_DeferredSelectEnumeratedLater
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        var customers = ids.Select(id => db.Customers.Single(c => c.Id == id));

        var names = new List<string>();
        foreach (var customer in customers)
            names.Add(customer.Name);

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q27_InMemorySumOfQueries
{
    public static int Run(ShopDb db)
    {
        var orders = db.Orders.AsNoTracking().ToList();
        return orders.Sum(order => db.OrderLines.Count(l => l.OrderId == order.Id));
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q28_InMemoryOrderByQueryKey
{
    public static List<Customer> Run(ShopDb db)
    {
        var customers = db.Customers.ToList();
        return customers.OrderByDescending(c => db.Orders.Count(o => o.CustomerId == c.Id)).ToList();
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q29_StringJoinOverSelect
{
    public static string Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        return string.Join(", ", ids.Select(id => db.Customers.Where(c => c.Id == id).Select(c => c.Name).First()));
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q30_AsyncSelectAwaitedInLoop
{
    public static async Task<List<string>> Run(ShopDb db)
    {
        var ids = await db.Customers.Select(c => c.Id).ToListAsync();
        var names = new List<string>();
        foreach (var pending in ids.Select(id => db.Customers.FirstAsync(c => c.Id == id)))
            names.Add((await pending).Name);

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q31_QuerySyntaxOverList
{
    public static List<string> Run(ShopDb db)
    {
        var orders = db.Orders.AsNoTracking().ToList();
        var labels =
            from order in orders
            let customer = db.Customers.First(c => c.Id == order.CustomerId)
            select $"{order.Id}: {customer.Name}";

        return labels.ToList();
    }
}

[NPlusOne(Kind.Query, Via.Lambda)]
public static class Q32_ArrayConvertAll
{
    public static string[] Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToArray();
        return System.Array.ConvertAll(ids, id => db.Customers.Where(c => c.Id == id).Select(c => c.Name).Single());
    }
}
