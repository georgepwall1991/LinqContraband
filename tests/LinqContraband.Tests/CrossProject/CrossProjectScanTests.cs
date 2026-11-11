using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Tests.CrossProject;

/// <summary>
/// The scanner's cross-project pass: a loop in one project, the query or save in another. Each project is compiled
/// on its own and sees the others as metadata, exactly as in a build, so LC007 and LC010 alone are quiet on every
/// case here.
/// </summary>
public class CrossProjectScanTests
{
    private static readonly ScanProject Domain = new("Shop.Domain", """
        namespace Shop.Domain
        {
            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public interface ICustomerRepository
            {
                Customer GetById(int id);
                Task<Customer> GetByIdAsync(int id);
                void Add(Customer customer);
                string Describe(int id);
            }
        }
        """);

    private static readonly ScanProject Data = new("Shop.Data", """
        using Shop.Domain;

        namespace Shop.Data
        {
            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; }
            }

            public class CustomerRepository : ICustomerRepository
            {
                private readonly ShopContext _db;

                public CustomerRepository(ShopContext db) => _db = db;

                public Customer GetById(int id) => _db.Customers.Find(id);

                public async Task<Customer> GetByIdAsync(int id) => await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);

                public void Add(Customer customer)
                {
                    _db.Customers.Add(customer);
                    _db.SaveChanges();
                }

                public string Describe(int id) => id.ToString();
            }
        }
        """);

    private static ScanProject App(string body, string members = "") => new("Shop.App", """
        using Shop.Domain;

        namespace Shop.App
        {
            public class Importer
            {
                private readonly ICustomerRepository customers;

                public Importer(ICustomerRepository customers) => this.customers = customers;

                public async Task Run(List<int> ids)
                {
        """ + body + """

                    await Task.CompletedTask;
                }
        """ + members + """
            }
        }
        """);

    [Fact]
    public async Task InterfaceImplementedInAnotherProject_ReportsThePerItemCall()
    {
        var findings = await CrossProjectScan.FindAsync(
            Domain,
            Data,
            App("""
                foreach (var id in ids)
                {
                    var customer = customers.GetById(id);
                }

                foreach (var id in ids)
                    await customers.GetByIdAsync(id);

                var names = ids.Select(id => customers.GetById(id).Name).ToList();
                var loaded = ids.Select(customers.GetById).ToList();

                foreach (var id in ids)
                    customers.Add(new Customer { Id = id });

                // Runs no query, is not in a loop, or works on a repository made per item.
                foreach (var id in ids)
                    Console.WriteLine(customers.Describe(id));
                var one = customers.GetById(1);
                """));

        Assert.Equal(
            [
                new ScanFinding("LC007", "var customer = customers.GetById(id);", "Warning", "'GetById' runs 'Find' in another project on every iteration of the loop, causing N+1 database operations. Fetch data in bulk or eager load before the loop."),
                new ScanFinding("LC007", "await customers.GetByIdAsync(id);", "Warning", "'GetByIdAsync' runs 'FirstOrDefaultAsync' in another project on every iteration of the loop, causing N+1 database operations. Fetch data in bulk or eager load before the loop."),
                new ScanFinding("LC007", "var names = ids.Select(id => customers.GetById(id).Name).ToList();", "Warning", "'GetById' runs 'Find' in another project for every element 'Select' processes, causing N+1 database operations. Fetch data in bulk or eager load before iterating."),
                new ScanFinding("LC007", "var loaded = ids.Select(customers.GetById).ToList();", "Warning", "'GetById' runs 'Find' in another project for every element 'Select' processes, causing N+1 database operations. Fetch data in bulk or eager load before iterating."),
                new ScanFinding("LC010", "customers.Add(new Customer { Id = id });", "Warning", "'Add' calls 'SaveChanges' in another project, so this saves once per item and causes N+1 database writes. Batch changes and save once after the loop."),
            ],
            findings);
    }

    [Fact]
    public async Task ClassAndStaticHelperOfAnotherProject_ReportThePerItemCall()
    {
        var data = new ScanProject("Shop.Data", """
            namespace Shop.Data
            {
                public class Order { public int Id { get; set; } public int CustomerId { get; set; } }

                public class ShopContext : DbContext
                {
                    public DbSet<Order> Orders { get; set; }
                }

                public class Repository<T> where T : class
                {
                    private readonly ShopContext _db;
                    public Repository(ShopContext db) => _db = db;
                    public T FindById(int id) => _db.Set<T>().Find(id);
                    public void AddAndSave(T entity) { _db.Set<T>().Add(entity); _db.SaveChanges(); }
                    public void AddOnly(T entity) => _db.Set<T>().Add(entity);
                }

                public static class OrderQueries
                {
                    public static int CountFor(this ShopContext db, int customerId) => db.Orders.Count(o => o.CustomerId == customerId);
                    public static bool Exists<T>(ShopContext db, int id) where T : class => db.Set<T>().Find(id) != null;
                }
            }
            """);
        var app = new ScanProject("Shop.App", """
            using Shop.Data;

            public class Reports
            {
                public void Run(ShopContext db, Repository<Order> orders, List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var order = orders.FindById(id);
                        var count = db.CountFor(id);
                        var exists = OrderQueries.Exists<Order>(db, id);
                        orders.AddAndSave(new Order { CustomerId = id });
                        orders.AddOnly(new Order { CustomerId = id });
                    }

                    // A repository over a context made for each item is that item's own unit of work.
                    foreach (var id in ids)
                        new Repository<Order>(new ShopContext()).AddAndSave(new Order { CustomerId = id });
                }
            }
            """);

        var findings = await CrossProjectScan.FindAsync(data, app);

        Assert.Equal(
            [
                ("LC007", "var order = orders.FindById(id);"),
                ("LC007", "var count = db.CountFor(id);"),
                ("LC007", "var exists = OrderQueries.Exists<Order>(db, id);"),
                ("LC010", "orders.AddAndSave(new Order { CustomerId = id });"),
            ],
            findings.Select(finding => (finding.Rule, finding.Code)));
    }

    [Fact]
    public async Task ServiceBetweenTheLoopAndTheRepository_IsFollowedAcrossProjects()
    {
        var services = new ScanProject("Shop.Services", """
            using Shop.Domain;

            namespace Shop.Services
            {
                public class CustomerService
                {
                    private readonly ICustomerRepository _customers;
                    public CustomerService(ICustomerRepository customers) => _customers = customers;

                    public Customer Load(int id) => LoadCore(id);
                    private Customer LoadCore(int id) => _customers.GetById(id);

                    public string Label(int id) => _customers.Describe(id);
                }
            }
            """);
        var app = new ScanProject("Shop.App", """
            using Shop.Services;

            public class Reports
            {
                public void Run(CustomerService service, List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var customer = service.Load(id);
                        var label = service.Label(id);
                    }
                }
            }
            """);

        var findings = await CrossProjectScan.FindAsync(Domain, Data, services, app);

        var finding = Assert.Single(findings);
        Assert.Equal(("LC007", "var customer = service.Load(id);"), (finding.Rule, finding.Code));
        Assert.StartsWith("'Load' runs 'Find' in another project on every iteration", finding.Message);
    }

    [Fact]
    public async Task InMemoryImplementation_KeepsTheInterfaceCallQuiet()
    {
        var fakes = new ScanProject("Shop.InMemory", """
            using Shop.Domain;

            public class InMemoryCustomerRepository : ICustomerRepository
            {
                private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
                public Customer GetById(int id) => _customers[id];
                public Task<Customer> GetByIdAsync(int id) => Task.FromResult(_customers[id]);
                public void Add(Customer customer) => _customers[customer.Id] = customer;
                public string Describe(int id) => id.ToString();
            }
            """);
        var app = App("""
            foreach (var id in ids)
            {
                var customer = customers.GetById(id);
                customers.Add(customer);
            }
            """);

        Assert.Empty(await CrossProjectScan.FindAsync(Domain, Data, fakes, app));

        // The same fake in a test project is not an implementation the application runs with.
        var findings = await CrossProjectScan.FindAsync(Domain, Data, fakes with { IsTestProject = true }, app);
        Assert.Equal(
            [("LC007", "var customer = customers.GetById(id);"), ("LC010", "customers.Add(customer);")],
            findings.Select(finding => (finding.Rule, finding.Code)));
    }

    [Fact]
    public async Task Decorators_ForwardingIsNeutralAndCachingIsQuiet()
    {
        const string loop = """
            foreach (var id in ids)
            {
                var customer = customers.GetById(id);
            }
            """;
        var logging = new ScanProject("Shop.Logging", """
            using Shop.Domain;

            public class LoggingCustomerRepository : ICustomerRepository
            {
                private readonly ICustomerRepository _inner;
                public LoggingCustomerRepository(ICustomerRepository inner) => _inner = inner;
                public Customer GetById(int id) { Console.WriteLine(id); return _inner.GetById(id); }
                public Task<Customer> GetByIdAsync(int id) => _inner.GetByIdAsync(id);
                public void Add(Customer customer) => _inner.Add(customer);
                public string Describe(int id) => _inner.Describe(id);
            }
            """);
        var caching = new ScanProject("Shop.Caching", """
            using Shop.Domain;

            public class CachedCustomerRepository : ICustomerRepository
            {
                private readonly ICustomerRepository _inner;
                private readonly Dictionary<int, Customer> _cache = new Dictionary<int, Customer>();
                public CachedCustomerRepository(ICustomerRepository inner) => _inner = inner;

                public Customer GetById(int id)
                {
                    if (_cache.TryGetValue(id, out var cached))
                        return cached;
                    return _cache[id] = _inner.GetById(id);
                }

                public Task<Customer> GetByIdAsync(int id) => _inner.GetByIdAsync(id);
                public void Add(Customer customer) => _inner.Add(customer);
                public string Describe(int id) => _inner.Describe(id);
            }
            """);

        var forwarded = await CrossProjectScan.FindAsync(Domain, Data, logging, App(loop));
        Assert.Equal([("LC007", "var customer = customers.GetById(id);")], forwarded.Select(finding => (finding.Rule, finding.Code)));

        Assert.Empty(await CrossProjectScan.FindAsync(Domain, Data, caching, App(loop)));
    }

    [Fact]
    public async Task VirtualMethodOfAnotherProject_NeedsEveryOverrideToQuery()
    {
        var data = new ScanProject("Shop.Data", """
            namespace Shop.Data
            {
                public class Order { public int Id { get; set; } }

                public class ShopContext : DbContext
                {
                    public DbSet<Order> Orders { get; set; }
                }

                public class OrderReader
                {
                    protected readonly ShopContext Db = new ShopContext();
                    public virtual Order Read(int id) => Db.Orders.Find(id);
                }

                public abstract class OrderSource
                {
                    public abstract Order Next(int id);
                }

                public class DatabaseOrderSource : OrderSource
                {
                    private readonly ShopContext _db = new ShopContext();
                    public override Order Next(int id) => _db.Orders.First(o => o.Id == id);
                }
            }
            """);
        var app = new ScanProject("Shop.App", """
            using Shop.Data;

            public class Reports
            {
                public void Run(OrderReader reader, OrderSource source, List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var read = reader.Read(id);
                        var next = source.Next(id);
                    }
                }
            }
            """);
        var plugin = new ScanProject("Shop.Plugin", """
            using Shop.Data;

            public class FixedOrderReader : OrderReader
            {
                public override Order Read(int id) => new Order { Id = id };
            }
            """);

        var findings = await CrossProjectScan.FindAsync(data, app);
        Assert.Equal(
            [("LC007", "var read = reader.Read(id);"), ("LC007", "var next = source.Next(id);")],
            findings.Select(finding => (finding.Rule, finding.Code)));

        var withPlugin = await CrossProjectScan.FindAsync(data, app, plugin);
        Assert.Equal([("LC007", "var next = source.Next(id);")], withPlugin.Select(finding => (finding.Rule, finding.Code)));
    }

    [Fact]
    public async Task SuppressionsAndSeveritiesOfTheRule_ApplyToTheCallSite()
    {
        var app = App(
            """
                foreach (var id in ids)
                    customers.GetById(id);

            #pragma warning disable LC007
                foreach (var id in ids)
                    customers.GetById(id + 1);
            #pragma warning restore LC007

            #pragma warning disable
                foreach (var id in ids)
                    customers.GetById(id + 2);
            #pragma warning restore

            #pragma warning disable LC010, CS0168
                foreach (var id in ids)
                    customers.Add(null);
            #pragma warning restore LC010, CS0168

                Quiet(ids);
            """,
            """

                    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "LC007:N+1", Justification = "Small list.")]
                    private void Quiet(List<int> ids)
                    {
                        foreach (var id in ids)
                            customers.GetById(id + 3);
                    }
            """);

        var findings = await CrossProjectScan.FindAsync(Domain, Data, app);
        Assert.Equal([("LC007", "customers.GetById(id);", "Warning")], findings.Select(finding => (finding.Rule, finding.Code, finding.Severity)));

        var asError = app with { EditorConfig = ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("LC007", ReportDiagnostic.Error) };
        Assert.Equal(
            [("LC007", "customers.GetById(id);", "Error")],
            (await CrossProjectScan.FindAsync(Domain, Data, asError)).Select(finding => (finding.Rule, finding.Code, finding.Severity)));

        var switchedOff = app with { EditorConfig = ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("LC007", ReportDiagnostic.Suppress) };
        Assert.Empty(await CrossProjectScan.FindAsync(Domain, Data, switchedOff));

        var noWarn = app with { NoWarn = ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("LC007", ReportDiagnostic.Suppress) };
        Assert.Empty(await CrossProjectScan.FindAsync(Domain, Data, noWarn));
    }

    [Fact]
    public async Task LoopsTheRulesExempt_StayQuiet()
    {
        var app = App("""
            // One save per batch.
            foreach (var batch in ids.Chunk(100))
                customers.Add(new Customer { Id = batch[0] });

            // The body runs at most once.
            foreach (var id in ids)
            {
                var customer = customers.GetById(id);
                return;
            }
            """);

        Assert.Empty(await CrossProjectScan.FindAsync(Domain, Data, app));
    }

    [Fact]
    public async Task PackageAssembliesAndNormalBuilds_ExportNothing()
    {
        // Without the scanner's option the exporter writes no fact at all.
        var loop = App("""
            foreach (var id in ids)
                customers.GetById(id);
            """);
        Assert.Empty(await CrossProjectScan.ExportAsync(exportFacts: false, Domain, Data, loop));

        // An assembly restored from a NuGet package folder is not another project of the solution.
        var packages = Directory.CreateTempSubdirectory("cross-project-packages-").FullName;
        try
        {
            var packaged = Domain with { ReferencePath = Path.Combine(packages, ".nuget", "packages", "shop.domain", "1.0.0", "lib", "net8.0", "Shop.Domain.dll") };
            var facts = await CrossProjectScan.ExportAsync(exportFacts: true, packaged, loop);
            Assert.DoesNotContain(facts, fact => fact.Path == "Shop.App.cs");
        }
        finally
        {
            Directory.Delete(packages, recursive: true);
        }
    }
}
