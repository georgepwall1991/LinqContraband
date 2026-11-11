using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NPlusOneBenchmark.External;

namespace NPlusOneBenchmark.Corpus;

// Code that looks like the N+1 cases but makes the same number of database round trips at any data size.
// A report on one of these is a false positive.

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S01_IncludeThenLoop
{
    public static List<string> Run(ShopDb db)
    {
        var names = new List<string>();
        foreach (var order in db.Orders.Include(o => o.Customer).ToList())
            names.Add(order.Customer.Name);

        return names;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S02_ThenIncludeNestedLoops
{
    public static int Run(ShopDb db)
    {
        var value = 0;
        var orders = db.Orders.Include(o => o.Lines).ThenInclude(l => l.Product).ToList();
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
                value += line.Product.PriceInCents * line.Quantity;
        }

        return value;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S03_ServerSideProjection
{
    public static List<string> Run(ShopDb db)
    {
        var labels = new List<string>();
        var rows = db.Orders.Select(o => new { o.Id, CustomerName = o.Customer.Name }).ToList();
        foreach (var row in rows)
            labels.Add($"{row.Id}: {row.CustomerName}");

        return labels;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S04_BatchContainsThenLookup
{
    public static List<string> Run(ShopDb db)
    {
        var orders = db.Orders.AsNoTracking().ToList();
        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionary(c => c.Id);

        var labels = new List<string>();
        foreach (var order in orders)
            labels.Add($"{order.Id}: {customers[order.CustomerId].Name}");

        return labels;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S05_QueryBeforeLoop
{
    public static int Run(ShopDb db)
    {
        var orders = db.Orders.AsNoTracking().ToList();
        var total = 0;
        foreach (var customer in db.Customers.AsNoTracking().ToList())
            total += orders.Count(o => o.CustomerId == customer.Id);

        return total;
    }
}

[Safe(Kind.Save, Via.Direct)]
public static class S06_AddInLoopSaveOnce
{
    public static void Run(ShopDb db)
    {
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            db.AuditEntries.Add(new AuditEntry { Message = $"seen {id}" });

        db.SaveChanges();
    }
}

[Safe(Kind.Save, Via.Direct)]
public static class S07_AddRangeSaveOnce
{
    public static void Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        db.AuditEntries.AddRange(ids.Select(id => new AuditEntry { Message = $"seen {id}" }));
        db.SaveChanges();
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S08_ForeachOverQuery
{
    public static int Run(ShopDb db)
    {
        var active = 0;
        foreach (var customer in db.Customers.AsNoTracking().Where(c => c.IsActive))
            active += customer.Name.Length;

        return active;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S09_FirstMatchThenBreak
{
    public static Customer? Run(ShopDb db)
    {
        Customer? found = null;
        foreach (var id in new[] { 1, 2, 3 })
        {
            found = db.Customers.FirstOrDefault(c => c.Id == id);
            if (found != null)
                break;
        }

        return found;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S10_RetryLoop
{
    public static int Run(ShopDb db)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return db.Orders.Count();
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
            }
        }
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S11_PagingLoop
{
    public static int Run(ShopDb db)
    {
        const int pageSize = 500;
        var seen = 0;
        for (var page = 0; ; page++)
        {
            var batch = db.Orders.AsNoTracking().OrderBy(o => o.Id).Skip(page * pageSize).Take(pageSize).ToList();
            seen += batch.Count;
            if (batch.Count < pageSize)
                break;
        }

        return seen;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S12_ChunkedBatches
{
    public static int Run(ShopDb db)
    {
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        var found = 0;
        foreach (var chunk in ids.Chunk(500))
            found += db.Customers.Count(c => chunk.Contains(c.Id));

        return found;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S13_MemoizedLookupOverFixedTable
{
    public static List<string> Run(ShopDb db)
    {
        var regionNames = new Dictionary<int, string>();
        var labels = new List<string>();
        foreach (var customer in db.Customers.AsNoTracking().ToList())
        {
            if (!regionNames.TryGetValue(customer.RegionId, out var region))
            {
                region = db.Regions.Where(r => r.Id == customer.RegionId).Select(r => r.Name).First();
                regionNames[customer.RegionId] = region;
            }

            labels.Add($"{customer.Name} ({region})");
        }

        return labels;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S14_FindOnAlreadyTrackedEntities
{
    public static List<string> Run(ShopDb db)
    {
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var names = new List<string>();
        foreach (var order in orders)
        {
            var customer = db.Customers.Find(order.CustomerId);
            names.Add(customer!.Name);
        }

        return names;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S15_LazyContextWithInclude
{
    public static List<string> Run(LazyShopDb db)
    {
        var names = new List<string>();
        foreach (var order in db.Orders.Include(o => o.Customer).ToList())
            names.Add(order.Customer.Name);

        return names;
    }
}

[Safe(Kind.LazyLoad, Via.Lambda)]
public static class S16_LazyContextProjection
{
    public static List<OrderDto> Run(LazyShopDb db) =>
        db.Orders.Select(o => new OrderDto(o.Id, o.Customer.Name)).ToList();
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S17_LazyReadOnSingleEntity
{
    public static string Run(LazyShopDb db)
    {
        var order = db.Orders.OrderBy(o => o.Id).First();
        return order.Customer.Name;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S18_SplitQueryInclude
{
    public static int Run(LazyShopDb db)
    {
        var lines = 0;
        foreach (var order in db.Orders.Include(o => o.Lines).AsSplitQuery().ToList())
            lines += order.Lines.Count;

        return lines;
    }
}

[Safe(Kind.Query, Via.Lambda)]
public static class S19_InMemoryLambdaOverLists
{
    public static List<int> Run(ShopDb db)
    {
        var customers = db.Customers.AsNoTracking().ToList();
        var orders = db.Orders.AsNoTracking().ToList();
        return customers.Select(c => orders.Count(o => o.CustomerId == c.Id)).ToList();
    }
}

[Safe(Kind.Query, Via.Lambda)]
public static class S20_SubqueryInsideServerQuery
{
    public static List<Customer> Run(ShopDb db) =>
        db.Customers.Where(c => db.Orders.Any(o => o.CustomerId == c.Id)).ToList();
}

[Safe(Kind.Query, Via.Lambda)]
public static class S21_CorrelatedCountInProjection
{
    public static int Run(ShopDb db)
    {
        var rows = db.Customers.Select(c => new { c.Name, Orders = c.Orders.Count() }).ToList();
        return rows.Sum(r => r.Orders);
    }
}

[Safe(Kind.Query, Via.Interface)]
public static class S22_InterfaceBackedByPreloadedData
{
    public static List<string> Run(ShopDb db)
    {
        ICustomerNames names = new PreloadedCustomerNames(db);
        var labels = new List<string>();
        foreach (var order in db.Orders.AsNoTracking().ToList())
            labels.Add($"{order.Id}: {names.NameOf(order.CustomerId)}");

        return labels;
    }
}

[Safe(Kind.Save, Via.Direct)]
public static class S23_PeriodicFlush
{
    public static void Run(ShopDb db)
    {
        var pending = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
        {
            db.AuditEntries.Add(new AuditEntry { Message = $"seen {id}" });
            if (++pending % 1000 == 0)
                db.SaveChanges();
        }

        db.SaveChanges();
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S24_LoopOverEnumValues
{
    public static Dictionary<OrderStatus, int> Run(ShopDb db)
    {
        var counts = new Dictionary<OrderStatus, int>();
        foreach (var status in Enum.GetValues<OrderStatus>())
            counts[status] = db.Orders.Count(o => o.Status == status);

        return counts;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S25_ExplicitLoadOnSingleEntity
{
    public static int Run(ShopDb db)
    {
        var order = db.Orders.OrderBy(o => o.Id).First();
        db.Entry(order).Collection(o => o.Lines).Load();

        var quantity = 0;
        foreach (var line in order.Lines)
            quantity += line.Quantity;

        return quantity;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S26_NavigationFixedUpBySecondQuery
{
    public static List<string> Run(ShopDb db)
    {
        db.Customers.Load();

        var names = new List<string>();
        foreach (var order in db.Orders.ToList())
            names.Add(order.Customer.Name);

        return names;
    }
}

[Safe(Kind.Save, Via.Direct)]
public static class S27_SetBasedExecuteUpdate
{
    public static int Run(ShopDb db) =>
        db.Products.Where(p => p.Stock > 0).ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock - 1));
}

[Safe(Kind.Query, Via.Direct)]
public static class S28_ComposeInLoopExecuteOnce
{
    public static int Run(ShopDb db)
    {
        IQueryable<Order> query = db.Orders;
        foreach (var status in new[] { OrderStatus.Shipped, OrderStatus.Paid })
            query = query.Where(o => o.Status != status);

        return query.ToList().Count;
    }
}

[Safe(Kind.Query, Via.Interface)]
public static class S29_InterfaceCallOutsideLoop
{
    public static int Run(ShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var orders = repository.AllOrders();
        var open = 0;
        foreach (var order in orders)
        {
            if (order.Status == OrderStatus.Open)
                open++;
        }

        return open;
    }
}

[Safe(Kind.Query, Via.Interface)]
public static class S30_RepositoryBatchMethod
{
    public static List<string> Run(ShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        return repository.GetByIds(ids).Select(c => c.Name).ToList();
    }
}

[Safe(Kind.Query, Via.Mediator)]
public static class S31_MediatorBatchRequest
{
    public static List<string> Run(ShopDb db)
    {
        var mediator = new Mediator(db);
        var ids = db.Customers.Select(c => c.Id).ToList();
        return mediator.Send<GetCustomerNames, List<string>>(new GetCustomerNames(ids));
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S32_AsyncMaterializeThenInMemoryLoop
{
    public static async Task<int> Run(ShopDb db)
    {
        var lines = await db.OrderLines.AsNoTracking().ToListAsync();
        var quantity = 0;
        foreach (var line in lines)
            quantity += line.Quantity;

        return quantity;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S33_LoadOnceInsideLoop
{
    public static int Run(ShopDb db)
    {
        List<Category>? categories = null;
        var matched = 0;
        foreach (var product in db.Products.AsNoTracking().ToList())
        {
            categories ??= db.Categories.AsNoTracking().ToList();
            if (categories.Any(c => c.Id == product.CategoryId))
                matched++;
        }

        return matched;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S34_ReturnOnFirstIteration
{
    public static Customer? Run(ShopDb db)
    {
        foreach (var id in db.Orders.OrderBy(o => o.Id).Select(o => o.CustomerId).Take(5).ToList())
            return db.Customers.Find(id);

        return null;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S35_LazyContextScalarOnly
{
    public static int Run(LazyShopDb db)
    {
        var sum = 0;
        foreach (var order in db.Orders.ToList())
            sum += order.CustomerId;

        return sum;
    }
}

[Safe(Kind.LazyLoad, Via.Direct)]
public static class S36_LazyContextPrincipalsPreloaded
{
    public static List<string> Run(LazyShopDb db)
    {
        db.Customers.Load();

        var names = new List<string>();
        foreach (var order in db.Orders.ToList())
            names.Add(order.Customer.Name);

        return names;
    }
}

[Safe(Kind.Query, Via.CrossProject)]
public static class S37_ExternalRepositoryCalledOnce
{
    public static List<string> Run(ShopDb db)
    {
        var customers = new ExternalRepository<Customer>(db);
        var labels = new List<string>();
        foreach (var customer in customers.ListWhere(c => c.IsActive))
            labels.Add(ExternalFormatting.Label(customer.Id, customer.Name));

        return labels;
    }
}

[Safe(Kind.Query, Via.Helper)]
public static class S38_PureHelperInLoop
{
    public static List<string> Run(ShopDb db)
    {
        var labels = new List<string>();
        foreach (var customer in db.Customers.AsNoTracking().ToList())
            labels.Add(Format(customer));

        return labels;
    }

    private static string Format(Customer customer) => $"{customer.Id}: {customer.Name}";
}

[Safe(Kind.LazyLoad, Via.CrossMethod)]
public static class S39_EntityPassedToHelperAfterInclude
{
    public static List<string> Run(ShopDb db)
    {
        var labels = new List<string>();
        foreach (var order in db.Orders.Include(o => o.Customer).ToList())
            labels.Add(Describe(order));

        return labels;
    }

    private static string Describe(Order order) => $"{order.Id}: {order.Customer.Name}";
}

[Safe(Kind.Query, Via.Delegate)]
public static class S40_DelegateOverPreloadedDictionary
{
    public static List<string> Run(ShopDb db)
    {
        var names = db.Customers.ToDictionary(c => c.Id, c => c.Name);
        Func<int, string> nameOf = id => names[id];

        var labels = new List<string>();
        foreach (var order in db.Orders.AsNoTracking().ToList())
            labels.Add(nameOf(order.CustomerId));

        return labels;
    }
}

[Safe(Kind.Query, Via.QueryableHelper)]
public static class S41_QueryableHelperExecutedOnce
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var order in OpenOrders(db).ToList())
            total += order.Id;

        return total;
    }

    private static IQueryable<Order> OpenOrders(ShopDb db) =>
        db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Open);
}

[Safe(Kind.Save, Via.Helper)]
public static class S42_HelperStagesSaveOnce
{
    public static void Run(ShopDb db)
    {
        foreach (var order in db.Orders.ToList())
            MarkShipped(order);

        db.SaveChanges();
    }

    private static void MarkShipped(Order order) => order.Status = OrderStatus.Shipped;
}

[Safe(Kind.Save, Via.Interface)]
public static class S43_UnitOfWorkCommitOnce
{
    public static int Run(ShopDb db)
    {
        IUnitOfWork unitOfWork = new UnitOfWork(db);
        foreach (var customer in db.Customers.ToList())
            customer.IsActive = !customer.IsActive;

        return unitOfWork.Commit();
    }
}

[Safe(Kind.Query, Via.Lambda)]
public static class S44_SelectManyServerSide
{
    public static int Run(ShopDb db)
    {
        var quantities = db.Customers.SelectMany(c => c.Orders).SelectMany(o => o.Lines).Select(l => l.Quantity).ToList();
        return quantities.Sum();
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S45_GroupedLookupBuiltOnce
{
    public static int Run(ShopDb db)
    {
        var linesByOrder = db.OrderLines.AsNoTracking().ToLookup(l => l.OrderId);
        var total = 0;
        foreach (var order in db.Orders.AsNoTracking().ToList())
            total += linesByOrder[order.Id].Sum(l => l.Quantity);

        return total;
    }
}

[Safe(Kind.Query, Via.Direct)]
public static class S46_CompiledQueryCalledOnce
{
    public static string Run(ShopDb db)
    {
        var customer = OrderQueries.CompiledCustomerById(db, 1);
        var label = "";
        foreach (var part in new[] { "a", "b", "c" })
            label += part + (customer?.Name ?? "");

        return label;
    }
}

[Safe(Kind.LazyLoad, Via.Lambda)]
public static class S47_InMemorySelectAfterInclude
{
    public static List<string> Run(ShopDb db)
    {
        var orders = db.Orders.Include(o => o.Customer).ThenInclude(c => c.Region).ToList();
        return orders.Select(o => $"{o.Customer.Name} ({o.Customer.Region.Name})").ToList();
    }
}

[Safe(Kind.Save, Via.Direct)]
public static class S48_RemoveRangeSaveOnce
{
    public static void Run(ShopDb db)
    {
        var lines = db.OrderLines.Where(l => l.Quantity > 1).ToList();
        foreach (var line in lines)
            db.OrderLines.Remove(line);

        db.SaveChanges();
    }
}
