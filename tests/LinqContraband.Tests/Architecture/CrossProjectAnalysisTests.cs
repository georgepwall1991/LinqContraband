using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using LinqContraband.Analyzers.LC004_IQueryableLeak;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Architecture;

/// <summary>
/// In the IDE (and in MSBuildWorkspace), a project reference is a compilation reference: symbols
/// declared in the referenced project keep their <c>DeclaringSyntaxReferences</c>, but those syntax
/// trees belong to another compilation. Calling <c>GetSemanticModel</c> on them throws, which surfaces
/// as AD0001 and silently disables the rule for the whole project. Single-compilation verifier tests
/// cannot produce that shape, so these tests build a data library and an app that references it
/// against real EF Core metadata and run every analyzer over the app.
/// </summary>
public class CrossProjectAnalysisTests
{
    private const string DataLibrarySource =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.ChangeTracking;
        using Microsoft.EntityFrameworkCore.Diagnostics;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Data
        {
            public interface ISoftDelete { bool IsDeleted { get; set; } }

            public class Customer : ISoftDelete
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string Email { get; set; } = "";
                public bool IsDeleted { get; set; }
                public int Balance { get; set; }
                public DateTime CreatedAt { get; set; }
                public List<Order> Orders { get; set; } = new();
                public Address? Address { get; set; }
            }

            public class Address
            {
                public int Id { get; set; }
                public string City { get; set; } = "";
            }

            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
                public Customer Customer { get; set; } = null!;
                public List<OrderLine> Lines { get; set; } = new();
                public decimal Total { get; set; }
                public byte[] Version { get; set; } = Array.Empty<byte>();
            }

            public class OrderLine
            {
                public int Id { get; set; }
                public int OrderId { get; set; }
                public Order Order { get; set; } = null!;
                public int Quantity { get; set; }
            }

            public class ShopContext : DbContext
            {
                public ShopContext(DbContextOptions options) : base(options) { }

                public DbSet<Customer> Customers => Set<Customer>();
                public DbSet<Order> Orders => Set<Order>();
                public DbSet<OrderLine> OrderLines { get; set; } = null!;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.ApplyConfiguration(new OrderConfiguration());
                    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopContext).Assembly);
                    modelBuilder.Entity<Customer>().Navigation(c => c.Address).AutoInclude();
                    modelBuilder.Entity<Order>()
                        .HasMany(o => o.Lines)
                        .WithOne(l => l.Order)
                        .OnDelete(DeleteBehavior.ClientCascade);
                }

                protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                {
                    optionsBuilder.AddInterceptors(new AuditInterceptor());
                }

                public override int SaveChanges()
                {
                    foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
                    {
                        if (entry.State == EntityState.Deleted)
                        {
                            entry.State = EntityState.Modified;
                            entry.Entity.IsDeleted = true;
                        }
                    }

                    return base.SaveChanges();
                }

                public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                {
                    foreach (var entry in ChangeTracker.Entries())
                    {
                        if (IsDeleted(entry))
                            Convert(entry);
                    }

                    return base.SaveChangesAsync(cancellationToken);
                }

                private static bool IsDeleted(EntityEntry entry) => entry.State == EntityState.Deleted;

                private static void Convert(EntityEntry entry)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }

            public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.HasKey(o => o.Id);
                    builder.Property(o => o.Version).IsRowVersion();
                    builder.Navigation(o => o.Customer).AutoInclude();
                    builder.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
                }
            }

            public abstract class ConfigurationBase<T> : IEntityTypeConfiguration<T> where T : class
            {
                public virtual void Configure(EntityTypeBuilder<T> builder) { }
            }

            public sealed class AuditInterceptor : SaveChangesInterceptor
            {
                public override InterceptionResult<int> SavingChanges(
                    DbContextEventData eventData,
                    InterceptionResult<int> result)
                {
                    foreach (var entry in eventData.Context!.ChangeTracker.Entries())
                    {
                        if (entry.State == EntityState.Deleted)
                            entry.State = EntityState.Modified;
                    }

                    return result;
                }
            }

            public static class ServiceRegistration
            {
                public static IServiceCollection AddShop(this IServiceCollection services) =>
                    services.AddDbContext<ShopContext>(options => options.AddInterceptors(new AuditInterceptor()));
            }

            public static class CustomerHelpers
            {
                public static readonly HashSet<string> KnownCities = new() { "London", "Paris" };
                public static readonly List<int> ReservedIds = new List<int> { 1, 2, 3 };
                public static readonly Func<Customer, bool> Filter = c => c.Balance > 0;

                public static void Print(IEnumerable<Customer> customers)
                {
                    foreach (var customer in customers)
                        Console.WriteLine(customer.Name);
                }

                public static int CountActive(IEnumerable<Customer> customers) => customers.Count(c => !c.IsDeleted);

                public static List<Customer> Materialize(IQueryable<Customer> customers) => customers.ToList();

                public static Task<int> CountAsync(ShopContext db) => db.Customers.CountAsync();

                public static async Task<List<Customer>> LoadAsync(ShopContext db, CancellationToken cancellationToken) =>
                    await db.Customers.ToListAsync(cancellationToken);

                public static Task SaveAsync(ShopContext db) => db.SaveChangesAsync();

                public static void Save(ShopContext db) => db.SaveChanges();

                public static void Touch(Customer customer) => customer.Balance += 1;

                public static void Track(ShopContext db, Customer customer) => db.Attach(customer);

                public static IQueryable<Customer> Active(ShopContext db) => db.Customers.Where(c => !c.IsDeleted);

                public static int Key(Customer customer) => customer.Id;
            }

            public abstract class RepositoryBase<T> where T : class
            {
                protected RepositoryBase(ShopContext db)
                {
                    Db = db;
                    Set = db.Set<T>();
                }

                protected ShopContext Db { get; }

                protected DbSet<T> Set { get; }

                public IQueryable<T> Query() => Set;

                public virtual void SaveAll() => Db.SaveChanges();

                public virtual Task SaveAllAsync() => Db.SaveChangesAsync();
            }

            public sealed class ShopContextFactory
            {
                public ShopContext Create() => new ShopContext(new DbContextOptionsBuilder<ShopContext>().Options);
            }
        }
        """;

    private const string AppSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Data;

        namespace Shop.App
        {
            public sealed class AppShopContext : ShopContext
            {
                public AppShopContext(DbContextOptions options) : base(options) { }

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    base.OnModelCreating(modelBuilder);
                    modelBuilder.ApplyConfiguration(new CustomerConfiguration());
                }
            }

            public sealed class CustomerConfiguration : ConfigurationBase<Customer>
            {
                public override void Configure(EntityTypeBuilder<Customer> builder)
                {
                    builder.HasKey(c => c.Id);
                    builder.HasMany(c => c.Orders).WithOne(o => o.Customer);
                }
            }

            public sealed class CustomerRepository : RepositoryBase<Customer>
            {
                public CustomerRepository(ShopContext db) : base(db) { }

                public Customer? ById(int id) => Query().FirstOrDefault(c => c.Id == id);

                public override void SaveAll()
                {
                    foreach (var customer in Query().ToList())
                    {
                        customer.Balance++;
                        base.SaveAll();
                    }
                }
            }

            public sealed class CustomerService
            {
                private static readonly List<string> BlockedNames = new() { "root", "admin" };
                private readonly ShopContext _db;
                private readonly AppShopContext _appDb;

                public CustomerService(ShopContext db, AppShopContext appDb)
                {
                    _db = db;
                    _appDb = appDb;
                }

                public void Leak()
                {
                    var query = _db.Customers.Where(c => c.Balance > 0);
                    CustomerHelpers.Print(query);
                    var count = CustomerHelpers.CountActive(_db.Customers);
                    var list = CustomerHelpers.Materialize(_db.Customers.OrderBy(c => c.Name));
                    Console.WriteLine(count + list.Count);
                }

                public async Task ConcurrentAsync(CancellationToken cancellationToken)
                {
                    await Task.WhenAll(CustomerHelpers.CountAsync(_db), CustomerHelpers.CountAsync(_db));
                    var load = CustomerHelpers.LoadAsync(_db, cancellationToken);
                    var save = CustomerHelpers.SaveAsync(_db);
                    await Task.WhenAll(load, save);

                    var tasks = new List<Task<int>>();
                    for (var i = 0; i < 3; i++)
                        tasks.Add(CustomerHelpers.CountAsync(_db));
                    await Task.WhenAll(tasks);
                }

                public void BulkDelete()
                {
                    _db.Customers.Where(c => c.Balance == 0).ExecuteDelete();
                    _db.Orders.ExecuteDelete();
                    _appDb.Customers.Where(c => c.IsDeleted).ExecuteDelete();
                    _db.Customers.Where(c => c.Balance < 0).ExecuteUpdate(s => s.SetProperty(c => c.Balance, 0));
                }

                public void RemoveMany()
                {
                    var stale = _db.Customers.Where(c => c.Id < 10).ToList();
                    _db.Customers.RemoveRange(stale);
                    _appDb.RemoveRange(_appDb.Orders.Where(o => o.Total == 0).ToList());
                    _db.SaveChanges();
                }

                public void Includes()
                {
                    var orders = _db.Orders.ToList();
                    foreach (var order in orders)
                        Console.WriteLine(order.Lines.Count + order.Customer.Name);

                    var customers = _appDb.Customers.Include(c => c.Orders).ThenInclude(o => o.Lines).ToList();
                    Console.WriteLine(customers.Sum(c => c.Orders.Count));
                }

                public void LostUpdate(int id)
                {
                    var customer = _db.Customers.First(c => c.Id == id);
                    customer.Balance += 10;
                    CustomerHelpers.Save(_db);

                    var other = _db.Customers.Single(c => c.Id == id + 1);
                    CustomerHelpers.Touch(other);
                    _db.SaveChanges();

                    var order = _db.Orders.First(o => o.Id == id);
                    order.Total = order.Total + 1;
                    _db.SaveChanges();
                }

                public async Task NoTrackingAsync(int id)
                {
                    var customer = _db.Customers.AsNoTracking().First(c => c.Id == id);
                    customer.Name = "renamed";
                    CustomerHelpers.Save(_db);
                    CustomerHelpers.Track(_db, customer);
                    _db.SaveChanges();

                    var detached = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == id);
                    detached.Email = "x@example.com";
                    await CustomerHelpers.SaveAsync(_db);

                    async Task Local()
                    {
                        customer.Balance = 1;
                        await CustomerHelpers.SaveAsync(_db);
                    }

                    await Local();
                }

                public Customer? Lookup(int id) => _db.Customers.FirstOrDefault(c => c.Id == id);

                public bool Known(string city, string name) =>
                    CustomerHelpers.KnownCities.Contains(city) &&
                    CustomerHelpers.ReservedIds.Contains(1) &&
                    !BlockedNames.Contains(name);

                public void NPlusOne()
                {
                    foreach (var customer in _db.Customers.ToList())
                    {
                        var count = _db.Orders.Count(o => o.CustomerId == customer.Id);
                        Console.WriteLine(count);
                    }
                }

                public void SaveInLoop(List<Customer> customers)
                {
                    foreach (var customer in customers)
                    {
                        _db.Add(customer);
                        CustomerHelpers.Save(_db);
                    }
                }

                public async Task HelperInLoopAsync(List<Customer> customers)
                {
                    foreach (var customer in customers)
                        Console.WriteLine(await CustomerHelpers.CountAsync(_db));
                }

                public void Queries(string name)
                {
                    var filtered = _db.Customers.Where(CustomerHelpers.Filter).ToList();
                    var active = CustomerHelpers.Active(_db).ToList();
                    var lower = _db.Customers.Where(c => c.Name.ToLower() == name.ToLower()).ToList();
                    var recent = _db.Customers.Where(c => c.CreatedAt > DateTime.Now).ToList();
                    var page = _db.Customers.Skip(10).Take(5).ToList();
                    var first = _db.Customers.OrderBy(c => c.Name).OrderBy(c => c.Id).First();
                    var any = _db.Customers.Count() > 0;
                    var keyed = _db.Customers.Where(c => c.Id == CustomerHelpers.Key(first)).FirstOrDefault();
                    var grouped = _db.Orders.GroupBy(o => o.CustomerId).Select(g => g.ToList()).ToList();
                    var nested = _db.Customers.Select(c => new { c.Id, Orders = c.Orders.ToList() }).ToList();
                    var raw = _db.Customers.FromSqlRaw($"SELECT * FROM Customers WHERE Name = '{name}'").ToList();
                    _db.Database.ExecuteSqlRaw("DELETE FROM Customers WHERE Name = '" + name + "'");
                    var ignored = _db.Customers.IgnoreQueryFilters().ToList();
                    var all = _db.Customers.ToList().Where(c => c.Balance > 0).ToList();
                    Console.WriteLine(filtered.Count + active.Count + lower.Count + recent.Count + page.Count +
                        (any ? 1 : 0) + (keyed?.Id ?? 0) + grouped.Count + nested.Count + raw.Count + ignored.Count + all.Count);
                }

                public async Task<int> CountWithoutTokenAsync() => await _db.Customers.CountAsync();

                public int SyncOverAsync() => CustomerHelpers.CountAsync(_db).Result;

                public IQueryable<Customer> Disposed()
                {
                    using var db = new ShopContextFactory().Create();
                    return db.Customers.Where(c => c.Balance > 0);
                }
            }
        }
        """;

    public static IEnumerable<object[]> Analyzers() =>
        typeof(IQueryableLeakAnalyzer).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(DiagnosticAnalyzer).IsAssignableFrom(type) &&
                           type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new object[] { type.FullName! });

    [Fact]
    public void CrossProjectCompilations_BuildWithoutErrors()
    {
        var (library, app) = CreateCompilations();

        Assert.Empty(library.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(app.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [MemberData(nameof(Analyzers))]
    public async Task Analyzer_WhenSymbolsLiveInReferencedProject_DoesNotThrow(string analyzerTypeName)
    {
        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(
            typeof(IQueryableLeakAnalyzer).Assembly.GetType(analyzerTypeName, throwOnError: true)!)!;
        var (_, app) = CreateCompilations();
        var exceptions = new ConcurrentBag<string>();

        var diagnostics = await app
            .WithAnalyzers(
                ImmutableArray.Create(analyzer),
                new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: (exception, _, _) => exceptions.Add(exception.ToString()),
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false))
            .GetAnalyzerDiagnosticsAsync();

        Assert.True(exceptions.IsEmpty, string.Join(Environment.NewLine + Environment.NewLine, exceptions.Distinct().Take(3)));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AD0001" or "AD0000");
    }

    private static (Compilation Library, Compilation App) CreateCompilations()
    {
        var references = GetMetadataReferences().ToList();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var library = CSharpCompilation.Create(
            "Shop.Data",
            new[] { CSharpSyntaxTree.ParseText(DataLibrarySource, parseOptions, path: "Shop.Data.cs") },
            references,
            options);

        // ToMetadataReference yields a compilation reference, exactly what an IDE workspace hands
        // an analyzer for a project reference: library symbols keep their source declarations.
        var app = CSharpCompilation.Create(
            "Shop.App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions, path: "Shop.App.cs") },
            references.Append(library.ToMetadataReference()),
            options);

        return (library, app);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator);

        foreach (var path in trustedPlatformAssemblies)
            yield return MetadataReference.CreateFromFile(path);

        // Copied by the test project's CopyEfCoreTestReferences target.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "efcore"), "*.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }
}
