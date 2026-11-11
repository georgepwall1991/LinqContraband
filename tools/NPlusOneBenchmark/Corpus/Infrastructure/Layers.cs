using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.Corpus;

// The layers a typical application puts between a loop and the database. Every method here is a
// single round trip (or none); the cases decide whether it is called once or once per element.

public interface ICustomerRepository
{
    Customer? GetById(int id);

    Task<Customer?> GetByIdAsync(int id);

    List<Customer> GetByIds(IReadOnlyCollection<int> ids);

    List<Order> OrdersFor(int customerId);

    List<Order> AllOrders();
}

/// <summary>An unsealed repository with non-virtual methods, as most hand-written repositories are.</summary>
public class CustomerRepository : ICustomerRepository
{
    private readonly ShopDb _db;

    public CustomerRepository(ShopDb db) => _db = db;

    public Customer? GetById(int id) => _db.Customers.FirstOrDefault(c => c.Id == id);

    public Task<Customer?> GetByIdAsync(int id) => _db.Customers.FirstOrDefaultAsync(c => c.Id == id);

    public List<Customer> GetByIds(IReadOnlyCollection<int> ids) =>
        _db.Customers.Where(c => ids.Contains(c.Id)).ToList();

    public List<Order> OrdersFor(int customerId) => _db.Orders.Where(o => o.CustomerId == customerId).ToList();

    public List<Order> AllOrders() => _db.Orders.ToList();

    public IQueryable<Order> QueryOrdersFor(int customerId) => _db.Orders.Where(o => o.CustomerId == customerId);
}

public sealed class SealedOrderRepository
{
    private readonly ShopDb _db;

    public SealedOrderRepository(ShopDb db) => _db = db;

    public int CountLines(int orderId) => _db.OrderLines.Count(l => l.OrderId == orderId);

    public void Record(string message)
    {
        _db.AuditEntries.Add(new AuditEntry { Message = message });
        _db.SaveChanges();
    }
}

public interface IRepository<T>
    where T : class
{
    T? GetById(int id);

    List<T> List(Expression<Func<T, bool>> predicate);

    void Add(T entity);
}

/// <summary>The generic repository base found in most layered templates: virtual, saving on every write.</summary>
public class EfRepository<T> : IRepository<T>
    where T : class
{
    protected ShopDb Db { get; }

    public EfRepository(ShopDb db) => Db = db;

    public virtual T? GetById(int id) => Db.Set<T>().Find(id);

    public virtual List<T> List(Expression<Func<T, bool>> predicate) => Db.Set<T>().Where(predicate).ToList();

    public virtual void Add(T entity)
    {
        Db.Set<T>().Add(entity);
        Db.SaveChanges();
    }
}

public abstract class ProductReader
{
    public abstract Product? Read(int id);
}

public sealed class DbProductReader : ProductReader
{
    private readonly ShopDb _db;

    public DbProductReader(ShopDb db) => _db = db;

    public override Product? Read(int id) => _db.Products.SingleOrDefault(p => p.Id == id);
}

public interface IUnitOfWork
{
    int Commit();
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ShopDb _db;

    public UnitOfWork(ShopDb db) => _db = db;

    public int Commit() => _db.SaveChanges();
}

/// <summary>One interface, two implementations that disagree: one queries per call, one never does.</summary>
public interface ICustomerNames
{
    string NameOf(int customerId);
}

public sealed class DbCustomerNames : ICustomerNames
{
    private readonly ShopDb _db;

    public DbCustomerNames(ShopDb db) => _db = db;

    public string NameOf(int customerId) =>
        _db.Customers.Where(c => c.Id == customerId).Select(c => c.Name).First();
}

public sealed class PreloadedCustomerNames : ICustomerNames
{
    private readonly Dictionary<int, string> _names;

    public PreloadedCustomerNames(ShopDb db) => _names = db.Customers.ToDictionary(c => c.Id, c => c.Name);

    public string NameOf(int customerId) => _names[customerId];
}

/// <summary>A service over a repository interface: the loop is two layers from the query.</summary>
public class CustomerService
{
    private readonly ICustomerRepository _customers;

    public CustomerService(ICustomerRepository customers) => _customers = customers;

    public string Describe(int customerId)
    {
        var customer = _customers.GetById(customerId);
        return customer == null ? "unknown" : customer.Name;
    }

    public int OrderCount(int customerId) => _customers.OrdersFor(customerId).Count;
}

public interface IRequest<TResponse>
{
}

public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    TResponse Handle(TRequest request);
}

public sealed record GetCustomerName(int CustomerId) : IRequest<string>;

public sealed record GetCustomerNames(IReadOnlyCollection<int> CustomerIds) : IRequest<List<string>>;

public sealed record RecordAudit(string Message) : IRequest<int>;

public sealed class GetCustomerNameHandler : IRequestHandler<GetCustomerName, string>
{
    private readonly ShopDb _db;

    public GetCustomerNameHandler(ShopDb db) => _db = db;

    public string Handle(GetCustomerName request) =>
        _db.Customers.Where(c => c.Id == request.CustomerId).Select(c => c.Name).First();
}

public sealed class GetCustomerNamesHandler : IRequestHandler<GetCustomerNames, List<string>>
{
    private readonly ShopDb _db;

    public GetCustomerNamesHandler(ShopDb db) => _db = db;

    public List<string> Handle(GetCustomerNames request) =>
        _db.Customers.Where(c => request.CustomerIds.Contains(c.Id)).Select(c => c.Name).ToList();
}

public sealed class RecordAuditHandler : IRequestHandler<RecordAudit, int>
{
    private readonly ShopDb _db;

    public RecordAuditHandler(ShopDb db) => _db = db;

    public int Handle(RecordAudit request)
    {
        _db.AuditEntries.Add(new AuditEntry { Message = request.Message });
        return _db.SaveChanges();
    }
}

/// <summary>A minimal mediator: requests are dispatched to handlers looked up by request type.</summary>
public sealed class Mediator
{
    private readonly Dictionary<Type, object> _handlers;

    public Mediator(ShopDb db)
    {
        _handlers = new Dictionary<Type, object>
        {
            [typeof(GetCustomerName)] = new GetCustomerNameHandler(db),
            [typeof(GetCustomerNames)] = new GetCustomerNamesHandler(db),
            [typeof(RecordAudit)] = new RecordAuditHandler(db),
        };
    }

    public TResponse Send<TRequest, TResponse>(TRequest request)
        where TRequest : IRequest<TResponse> =>
        ((IRequestHandler<TRequest, TResponse>)_handlers[typeof(TRequest)]).Handle(request);
}

public static class OrderQueries
{
    public static int CountLines(ShopDb db, int orderId) => db.OrderLines.Count(l => l.OrderId == orderId);

    public static IQueryable<Order> ForCustomer(this IQueryable<Order> orders, int customerId) =>
        orders.Where(o => o.CustomerId == customerId);

    public static Customer? CustomerById(this ShopDb db, int id) => db.Customers.FirstOrDefault(c => c.Id == id);

    public static readonly Func<ShopDb, int, Customer?> CompiledCustomerById =
        EF.CompileQuery((ShopDb db, int id) => db.Customers.FirstOrDefault(c => c.Id == id));
}

public sealed record OrderDto(int Id, string CustomerName);
