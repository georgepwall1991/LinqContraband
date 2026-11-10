using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NPlusOneBenchmark.External;

namespace NPlusOneBenchmark.Corpus;

// Query per element, with the query one or more layers away from the loop.

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q40_StaticHelper
{
    public static List<string> Run(ShopDb db)
    {
        var names = new List<string>();
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            names.Add(NameOf(db, id));

        return names;
    }

    private static string NameOf(ShopDb db, int id) =>
        db.Customers.Where(c => c.Id == id).Select(c => c.Name).First();
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q41_InstanceHelper
{
    public static int Run(ShopDb db) => new Report(db).Build();

    private sealed class Report
    {
        private readonly ShopDb _db;

        public Report(ShopDb db) => _db = db;

        public int Build()
        {
            var total = 0;
            foreach (var customer in _db.Customers.AsNoTracking().ToList())
                total += OrderCount(customer.Id);

            return total;
        }

        private int OrderCount(int customerId) => _db.Orders.Count(o => o.CustomerId == customerId);
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q42_ExtensionMethodHelper
{
    public static List<string> Run(ShopDb db)
    {
        var names = new List<string>();
        foreach (var id in db.Orders.Select(o => o.CustomerId).Distinct().ToList())
        {
            var customer = db.CustomerById(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q43_LocalFunctionHelper
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var order in db.Orders.AsNoTracking().ToList())
            total += Lines(order.Id);

        return total;

        int Lines(int orderId) => db.OrderLines.Count(l => l.OrderId == orderId);
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q44_HelperChainFourDeep
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var order in db.Orders.AsNoTracking().ToList())
            total += Summarize(db, order.Id);

        return total;
    }

    private static int Summarize(ShopDb db, int orderId) => Measure(db, orderId);

    private static int Measure(ShopDb db, int orderId) => Tally(db, orderId);

    private static int Tally(ShopDb db, int orderId) => CountLines(db, orderId);

    private static int CountLines(ShopDb db, int orderId) => db.OrderLines.Count(l => l.OrderId == orderId);
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q45_ConcreteRepository
{
    public static int Run(ShopDb db)
    {
        var repository = new CustomerRepository(db);
        var total = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            total += repository.OrdersFor(id).Count;

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q46_SealedRepository
{
    public static int Run(ShopDb db)
    {
        var repository = new SealedOrderRepository(db);
        var total = 0;
        foreach (var orderId in db.Orders.Select(o => o.Id).ToList())
            total += repository.CountLines(orderId);

        return total;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q47_AsyncHelper
{
    public static async Task<List<string>> Run(ShopDb db)
    {
        var names = new List<string>();
        foreach (var id in await db.Customers.Select(c => c.Id).ToListAsync())
            names.Add(await NameOfAsync(db, id));

        return names;
    }

    private static async Task<string> NameOfAsync(ShopDb db, int id)
    {
        var customer = await db.Customers.FirstAsync(c => c.Id == id);
        return customer.Name;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q48_MethodGroupInSelect
{
    public static List<int> Run(ShopDb db)
    {
        var repository = new SealedOrderRepository(db);
        var orderIds = db.Orders.Select(o => o.Id).ToList();
        return orderIds.Select(repository.CountLines).ToList();
    }
}

[NPlusOne(Kind.Query, Via.Direct)]
public static class Q49_LoopInsideHelperCalledOnce
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        return Resolve(db, ids);
    }

    private static List<string> Resolve(ShopDb db, List<int> ids)
    {
        var names = new List<string>();
        foreach (var id in ids)
            names.Add(db.Customers.Where(c => c.Id == id).Select(c => c.Name).Single());

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q50_StaticHelperInAnotherClass
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var orderId in db.Orders.Select(o => o.Id).ToList())
            total += OrderQueries.CountLines(db, orderId);

        return total;
    }
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q51_HelperReturnsQueryable
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            total += OrdersOf(db, id).Count();

        return total;
    }

    private static IQueryable<Order> OrdersOf(ShopDb db, int customerId) =>
        db.Orders.Where(o => o.CustomerId == customerId);
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q52_QueryableExtensionMethod
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            total += db.Orders.ForCustomer(id).ToList().Count;

        return total;
    }
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q53_QueryableParameter
{
    public static int Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        return CountAll(db.Orders, ids);
    }

    private static int CountAll(IQueryable<Order> orders, List<int> customerIds)
    {
        var total = 0;
        foreach (var id in customerIds)
            total += orders.Count(o => o.CustomerId == id);

        return total;
    }
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q54_DbSetParameter
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        return Names(db.Customers, ids);
    }

    private static List<string> Names(DbSet<Customer> customers, List<int> ids)
    {
        var names = new List<string>();
        foreach (var id in ids)
        {
            var customer = customers.Find(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q55_RepositoryReturnsQueryable
{
    public static int Run(ShopDb db)
    {
        var repository = new CustomerRepository(db);
        var open = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            open += repository.QueryOrdersFor(id).Count(o => o.Status == OrderStatus.Open);

        return open;
    }
}

[NPlusOne(Kind.Query, Via.QueryableHelper)]
public static class Q56_QueryablePropertyOnService
{
    public static int Run(ShopDb db)
    {
        var catalog = new Catalog(db);
        var total = 0;
        foreach (var productId in db.Products.Select(p => p.Id).ToList())
            total += catalog.Products.Count(p => p.Id == productId);

        return total;
    }

    private sealed class Catalog
    {
        private readonly ShopDb _db;

        public Catalog(ShopDb db) => _db = db;

        public IQueryable<Product> Products => _db.Products.AsNoTracking();
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q60_InterfaceRepository
{
    public static List<string> Run(ShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var names = new List<string>();
        foreach (var id in db.Orders.Select(o => o.CustomerId).Distinct().ToList())
        {
            var customer = repository.GetById(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q61_ServiceOverInterfaceRepository
{
    public static List<string> Run(ShopDb db)
    {
        var service = new CustomerService(new CustomerRepository(db));
        var descriptions = new List<string>();
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            descriptions.Add(service.Describe(id));

        return descriptions;
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q62_GenericInterfaceRepository
{
    public static int Run(ShopDb db)
    {
        IRepository<Product> products = new EfRepository<Product>(db);
        var stock = 0;
        foreach (var line in db.OrderLines.AsNoTracking().ToList())
            stock += products.List(p => p.Id == line.ProductId).Count;

        return stock;
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q63_InterfaceInSelectLambda
{
    public static List<Customer?> Run(ShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var ids = db.Orders.Select(o => o.CustomerId).Distinct().ToList();
        return ids.Select(id => repository.GetById(id)).ToList();
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q64_AsyncInterfaceRepository
{
    public static async Task<List<string>> Run(ShopDb db)
    {
        ICustomerRepository repository = new CustomerRepository(db);
        var names = new List<string>();
        foreach (var id in await db.Customers.Select(c => c.Id).ToListAsync())
        {
            var customer = await repository.GetByIdAsync(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q65_InterfaceFieldInjected
{
    public static int Run(ShopDb db) => new Handler(new CustomerRepository(db), db).Handle();

    private sealed class Handler
    {
        private readonly ICustomerRepository _customers;
        private readonly ShopDb _db;

        public Handler(ICustomerRepository customers, ShopDb db)
        {
            _customers = customers;
            _db = db;
        }

        public int Handle()
        {
            var total = 0;
            foreach (var id in _db.Customers.Select(c => c.Id).ToList())
                total += _customers.OrdersFor(id).Count;

            return total;
        }
    }
}

[NPlusOne(Kind.Query, Via.Interface)]
public static class Q66_InterfaceWithDisagreeingImplementations
{
    public static List<string> Run(ShopDb db)
    {
        ICustomerNames names = new DbCustomerNames(db);
        var labels = new List<string>();
        foreach (var order in db.Orders.AsNoTracking().ToList())
            labels.Add($"{order.Id}: {names.NameOf(order.CustomerId)}");

        return labels;
    }
}

[NPlusOne(Kind.Query, Via.Virtual)]
public static class Q70_VirtualRepositoryMethod
{
    public static int Run(ShopDb db)
    {
        var products = new EfRepository<Product>(db);
        var value = 0;
        foreach (var line in db.OrderLines.AsNoTracking().ToList())
        {
            var product = products.GetById(line.ProductId);
            value += (product?.PriceInCents ?? 0) * line.Quantity;
        }

        return value;
    }
}

[NPlusOne(Kind.Query, Via.Virtual)]
public static class Q71_AbstractMethodOneImplementation
{
    public static int Run(ShopDb db)
    {
        ProductReader reader = new DbProductReader(db);
        var stock = 0;
        foreach (var id in db.Products.Select(p => p.Id).ToList())
            stock += reader.Read(id)?.Stock ?? 0;

        return stock;
    }
}

[NPlusOne(Kind.Query, Via.Helper)]
public static class Q72_GenericSetHelper
{
    public static int Run(ShopDb db)
    {
        var found = 0;
        foreach (var id in db.Products.Select(p => p.Id).ToList())
        {
            if (Load<Product>(db, id) != null)
                found++;
        }

        return found;
    }

    private static T? Load<T>(DbContext db, int id)
        where T : class => db.Set<T>().Find(id);
}

[NPlusOne(Kind.Query, Via.Delegate)]
public static class Q75_LocalFuncDelegate
{
    public static List<string> Run(ShopDb db)
    {
        Func<int, string> nameOf = id => db.Customers.Where(c => c.Id == id).Select(c => c.Name).First();

        var names = new List<string>();
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            names.Add(nameOf(id));

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Delegate)]
public static class Q76_CallbackPassedToIterator
{
    public static int Run(ShopDb db)
    {
        var total = 0;
        var ids = db.Customers.Select(c => c.Id).ToList();
        Each(ids, id => total += db.Orders.Count(o => o.CustomerId == id));
        return total;
    }

    private static void Each<T>(IEnumerable<T> items, Action<T> action)
    {
        foreach (var item in items)
            action(item);
    }
}

[NPlusOne(Kind.Query, Via.Delegate)]
public static class Q77_DelegateParameter
{
    public static List<string> Run(ShopDb db)
    {
        var ids = db.Customers.Select(c => c.Id).ToList();
        return Map(ids, id => db.Customers.Where(c => c.Id == id).Select(c => c.Name).First());
    }

    private static List<string> Map(List<int> ids, Func<int, string> resolve)
    {
        var names = new List<string>();
        foreach (var id in ids)
            names.Add(resolve(id));

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Mediator)]
public static class Q80_MediatorSendPerElement
{
    public static List<string> Run(ShopDb db)
    {
        var mediator = new Mediator(db);
        var names = new List<string>();
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            names.Add(mediator.Send<GetCustomerName, string>(new GetCustomerName(id)));

        return names;
    }
}

[NPlusOne(Kind.Query, Via.Mediator)]
public static class Q81_MediatorSendInSelect
{
    public static List<string> Run(ShopDb db)
    {
        var mediator = new Mediator(db);
        var ids = db.Customers.Select(c => c.Id).ToList();
        return ids.Select(id => mediator.Send<GetCustomerName, string>(new GetCustomerName(id))).ToList();
    }
}

[NPlusOne(Kind.Query, Via.CrossProject)]
public static class Q85_ExternalRepositoryFind
{
    public static List<string> Run(ShopDb db)
    {
        var customers = new ExternalRepository<Customer>(db);
        var names = new List<string>();
        foreach (var id in db.Orders.Select(o => o.CustomerId).Distinct().ToList())
        {
            var customer = customers.FindById(id);
            if (customer != null)
                names.Add(customer.Name);
        }

        return names;
    }
}

[NPlusOne(Kind.Query, Via.CrossProject)]
public static class Q86_ExternalRepositoryQueryable
{
    public static int Run(ShopDb db)
    {
        var orders = new ExternalRepository<Order>(db);
        var total = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            total += orders.Query().Where(o => o.CustomerId == id).ToList().Count;

        return total;
    }
}

[NPlusOne(Kind.Query, Via.CrossProject)]
public static class Q87_ExternalRepositoryInSelect
{
    public static List<int> Run(ShopDb db)
    {
        var orders = new ExternalRepository<Order>(db);
        var ids = db.Customers.Select(c => c.Id).ToList();
        return ids.Select(id => orders.CountWhere(o => o.CustomerId == id)).ToList();
    }
}

[NPlusOne(Kind.Query, Via.CrossProject)]
public static class Q88_ExternalStaticHelper
{
    public static int Run(ShopDb db)
    {
        var withOrders = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
        {
            if (ExternalQueries.Exists<Order>(db, o => o.CustomerId == id))
                withOrders++;
        }

        return withOrders;
    }
}
