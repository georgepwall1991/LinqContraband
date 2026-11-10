using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.Corpus;

// Query per element, written directly in a loop statement.

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q01_ForeachFirstById
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        var names = new List<string>();
        foreach (var id in ids)
        {
            var customer = db.Customers.First(c => c.Id == id);
            names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q02_ForeachChildListPerParent
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        var customers = db.Customers.ToList();
        foreach (var customer in customers)
        {
            var orders = db.Orders.Where(o => o.CustomerId == customer.Id).ToList();
            total += orders.Count;
        }

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q03_ForLoopCount
{
    public static int Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToArray();
        var total = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            total += db.Orders.Count(o => o.CustomerId == id);
        }

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q04_WhileQueueFind
{
    public static int Run(ShopDb db)
    {
        var pending = new Queue<int>(db.Products.Select(p => p.Id).ToList());
        var stock = 0;
        while (pending.Count > 0)
        {
            var product = db.Products.Find(pending.Dequeue());
            stock += product?.Stock ?? 0;
        }

        return stock;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q05_AsyncForeachToListAsync
{
    public static async Task<int> Run(ShopDb db)
    {
        var customers = await db.Customers.AsNoTracking().ToListAsync();
        var total = 0;
        foreach (var customer in customers)
        {
            var orders = await db.Orders.Where(o => o.CustomerId == customer.Id).ToListAsync();
            total += orders.Count;
        }

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q06_ForeachFind
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        var names = new List<string>();
        foreach (var id in ids)
        {
            var customer = db.Customers.Find(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q07_ForeachAnyCheck
{
    public static List<Customer> Run(ShopDb db)
    {
        var withOpenOrders = new List<Customer>();
        foreach (var customer in db.Customers.ToList())
        {
            if (db.Orders.Any(o => o.CustomerId == customer.Id && o.Status == OrderStatus.Open))
                withOpenOrders.Add(customer);
        }

        return withOpenOrders;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q08_NestedLoopInnerQuery
{
    public static int Run(ShopDb db)
    {
        var value = 0;
        var orders = db.Orders.Include(o => o.Lines).ToList();
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
            {
                var product = db.Products.Single(p => p.Id == line.ProductId);
                value += product.PriceInCents * line.Quantity;
            }
        }

        return value;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q09_ExplicitCollectionLoad
{
    public static int Run(ShopDb db)
    {
        var lines = 0;
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            db.Entry(order).Collection(o => o.Lines).Load();
            lines += order.Lines.Count;
        }

        return lines;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q10_ExplicitReferenceLoad
{
    public static List<string> Run(ShopDb db)
    {
        var names = new List<string>();
        var lines = db.OrderLines.ToList();
        foreach (var line in lines)
        {
            db.Entry(line).Reference(l => l.Product).Load();
            names.Add(line.Product.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q11_QueryWhileStreamingAnother
{
    public static List<string> Run(ShopDb db)
    {
        var labels = new List<string>();
        foreach (var order in db.Orders.AsNoTracking())
        {
            var name = db.Customers.Where(c => c.Id == order.CustomerId).Select(c => c.Name).First();
            labels.Add($"{order.Id}: {name}");
        }

        return labels;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q12_RawSqlPerElement
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        var ids = db.Customers.Select(c => c.Id).ToList();
        foreach (var id in ids)
        {
            var orders = db.Orders.FromSql($"SELECT * FROM Orders WHERE CustomerId = {id}").ToList();
            total += orders.Count;
        }

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q13_CompiledQueryPerElement
{
    public static List<string> Run(ShopDb db)
    {
        var names = new List<string>();
        var ids = db.Customers.Select(c => c.Id).ToList();
        foreach (var id in ids)
        {
            var customer = OrderQueries.CompiledCustomerById(db, id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q14_AggregatePerElement
{
    public static Dictionary<int, int> Run(ShopDb db)
    {
        var quantities = new Dictionary<int, int>();
        var orders = db.Orders.AsNoTracking().ToList();
        foreach (var order in orders)
            quantities[order.Id] = db.OrderLines.Where(l => l.OrderId == order.Id).Sum(l => l.Quantity);

        return quantities;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q15_BaseQueryBuiltOutsideLoop
{
    public static int Run(ShopDb db)
    {
        var open = db.Orders.Where(o => o.Status == OrderStatus.Open);
        var total = 0;
        foreach (var customerId in db.Customers.Select(c => c.Id).ToList())
            total += open.Where(o => o.CustomerId == customerId).ToList().Count;

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q16_DoWhileCursor
{
    public static int Run(ShopDb db)
    {
        var maxId = db.Customers.Max(c => c.Id);
        var id = 0;
        var active = 0;
        do
        {
            id++;
            var customer = db.Customers.SingleOrDefault(c => c.Id == id);
            if (customer != null && customer.IsActive)
                active++;
        }
        while (id < maxId);

        return active;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q17_QueryInsideSwitchInLoop
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var order in db.Orders.AsNoTracking().ToList())
        {
            switch (order.Status)
            {
                case OrderStatus.Open:
                case OrderStatus.Paid:
                case OrderStatus.Shipped:
                    total += db.OrderLines.Count(l => l.OrderId == order.Id);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q18_AwaitForeachInnerQuery
{
    public static async Task<List<string>> Run(ShopDb db)
    {
        var labels = new List<string>();
        await foreach (var order in db.Orders.AsNoTracking().AsAsyncEnumerable())
        {
            var customer = await db.Customers.FirstAsync(c => c.Id == order.CustomerId);
            labels.Add($"{order.Id}: {customer.Name}");
        }

        return labels;
    }
}
