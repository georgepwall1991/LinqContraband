using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.Corpus;

// A navigation read per element on a lazy-loading context: each read of an unloaded navigation is a query.

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L01_ReferenceInForeach
{
    public static List<string> Run(LazyShopDb db)
    {
        var names = new List<string>();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
            names.Add(order.Customer.Name);

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L02_CollectionCountInForeach
{
    public static int Run(LazyShopDb db)
    {
        var total = 0;
        foreach (var customer in db.Customers.ToList())
            total += customer.Orders.Count;

        return total;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L03_NestedCollectionThenReference
{
    public static int Run(LazyShopDb db)
    {
        var value = 0;
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
                value += line.Product.PriceInCents * line.Quantity;
        }

        return value;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Lambda)]
public static class L04_SelectOverMaterializedList
{
    public static List<string> Run(LazyShopDb db)
    {
        var orders = db.Orders.ToList();
        return orders.Select(o => o.Customer.Name).ToList();
    }
}

[NPlusOne(Kind.LazyLoad, Via.Lambda)]
public static class L05_SumOfCollectionCounts
{
    public static int Run(LazyShopDb db)
    {
        var orders = db.Orders.ToList();
        return orders.Sum(o => o.Lines.Count);
    }
}

[NPlusOne(Kind.LazyLoad, Via.CrossMethod)]
public static class L06_EntityPassedToHelper
{
    public static List<string> Run(LazyShopDb db)
    {
        var labels = new List<string>();
        foreach (var order in db.Orders.ToList())
            labels.Add(Describe(order));

        return labels;
    }

    private static string Describe(Order order) => $"{order.Id}: {order.Customer.Name}";
}

[NPlusOne(Kind.LazyLoad, Via.CrossMethod)]
public static class L07_EntityMemberReadsNavigation
{
    public static List<string> Run(LazyShopDb db)
    {
        var labels = new List<string>();
        foreach (var order in db.Orders.ToList())
            labels.Add(order.CustomerLabel);

        return labels;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L08_TwoLevelReferenceChain
{
    public static List<string> Run(LazyShopDb db)
    {
        var labels = new List<string>();
        foreach (var order in db.Orders.ToList())
            labels.Add($"{order.Id}: {order.Customer.Region.Name}");

        return labels;
    }
}

[NPlusOne(Kind.LazyLoad, Via.CrossMethod)]
public static class L09_MapperMethodGroup
{
    public static List<OrderDto> Run(LazyShopDb db)
    {
        var orders = db.Orders.ToList();
        return orders.Select(ToDto).ToList();
    }

    private static OrderDto ToDto(Order order) => new OrderDto(order.Id, order.Customer.Name);
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L10_NavigationInCondition
{
    public static int Run(LazyShopDb db)
    {
        var active = 0;
        foreach (var order in db.Orders.Where(o => o.Status != OrderStatus.Shipped).ToList())
        {
            if (order.Customer.IsActive)
                active++;
        }

        return active;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L11_AsyncMaterializeThenLoop
{
    public static async Task<List<string>> Run(LazyShopDb db)
    {
        var names = new List<string>();
        var lines = await db.OrderLines.ToListAsync();
        foreach (var line in lines)
            names.Add(line.Product.Name);

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L12_ForLoopIndexer
{
    public static List<string> Run(LazyShopDb db)
    {
        var names = new List<string>();
        var orders = db.Orders.OrderBy(o => o.Id).ToList();
        for (var i = 0; i < orders.Count; i++)
            names.Add(orders[i].Customer.Name);

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Lambda)]
public static class L13_InMemoryGroupByNavigation
{
    public static Dictionary<string, int> Run(LazyShopDb db)
    {
        var orders = db.Orders.ToList();
        return orders.GroupBy(o => o.Customer.Name).ToDictionary(g => g.Key, g => g.Count());
    }
}

[NPlusOne(Kind.LazyLoad, Via.Lambda)]
public static class L14_InMemoryWhereOnCollection
{
    public static List<Customer> Run(LazyShopDb db)
    {
        var customers = db.Customers.ToList();
        return customers.Where(c => c.Orders.Any(o => o.Status == OrderStatus.Open)).ToList();
    }
}

[NPlusOne(Kind.LazyLoad, Via.CrossMethod)]
public static class L15_EntitiesReturnedByRepository
{
    public static List<string> Run(LazyShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var names = new List<string>();
        foreach (var order in repository.AllOrders())
            names.Add(order.Customer.Name);

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.CrossMethod)]
public static class L16_ListPassedToAnotherMethod
{
    public static List<string> Run(LazyShopDb db)
    {
        var orders = db.Orders.ToList();
        return Names(orders);
    }

    private static List<string> Names(List<Order> orders)
    {
        var names = new List<string>();
        foreach (var order in orders)
            names.Add(order.Customer.Name);

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L17_StreamedQueryLoop
{
    public static int Run(LazyShopDb db)
    {
        var lines = 0;
        foreach (var order in db.Orders)
            lines += order.Lines.Count;

        return lines;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L18_NestedLoopOverLazyCollection
{
    public static List<string> Run(LazyShopDb db)
    {
        var names = new List<string>();
        var customer = db.Customers.OrderBy(c => c.Id).First();
        foreach (var other in db.Customers.Where(c => c.RegionId == customer.RegionId).ToList())
        {
            foreach (var order in other.Orders)
                names.Add($"{other.Name}: {order.Id}");
        }

        return names;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Lambda)]
public static class L19_AnonymousProjectionOverList
{
    public static int Run(LazyShopDb db)
    {
        var lines = db.OrderLines.ToList();
        var rows = lines.Select(l => new { l.Id, Product = l.Product.Name }).ToList();
        return rows.Count;
    }
}

[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L20_IncludeCoversOnlyFirstLevel
{
    public static int Run(LazyShopDb db)
    {
        var value = 0;
        var orders = db.Orders.Include(o => o.Lines).ToList();
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
                value += line.Product.PriceInCents;
        }

        return value;
    }
}

/// <summary>
/// The other direction of S36. Loading every order first fills <c>customer.Orders</c> by fix-up, but
/// EF Core does not mark a collection loaded that way, so the proxy still queries once per customer.
/// </summary>
[NPlusOne(Kind.LazyLoad, Via.Direct)]
public static class L21_DependentsPreloadedCollectionRead
{
    public static int Run(LazyShopDb db)
    {
        db.Orders.Load();

        var total = 0;
        foreach (var customer in db.Customers.ToList())
            total += customer.Orders.Count;

        return total;
    }
}
