using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A table loaded whole into the context before the query: <c>db.Customers.Load();</c>. EF Core fixes up
/// <c>order.Customer</c> to the tracked customer when it materializes each order, so the reference is populated
/// without an <c>Include</c>, and a lazy-loading proxy does not query for it again. That holds for reference
/// navigations only: a collection filled by fix-up is not marked loaded, and a lazy-loading context still queries
/// once per owner.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("db.Customers.Load();", "db.Orders.ToList()")]
    [InlineData("db.Set<Customer>().Load();", "db.Orders.Where(o => o.Id > 0).ToList()")]
    [InlineData("db.Customers.Include(c => c.Address).Load();", "db.Orders.ToList()")]
    [InlineData("db.Customers.OrderBy(c => c.Id).AsTracking().Load();", "db.Orders.AsTracking().ToList()")]
    public async Task TestInnocent_PrincipalsLoadedBeforeTheQuery_StaysQuiet(string preload, string query)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        "
                + preload
                + @"

        var orders = "
                + query
                + @";
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name + order.BillingCustomer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_PrincipalsLoadedAsyncBeforeTheQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    private readonly MyDbContext _db = new MyDbContext();

    async Task Main()
    {
        await _db.Customers.LoadAsync();

        var orders = await _db.Orders.ToListAsync();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_PrincipalsLoadedBeforeAForeachOverTheSet_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        db.Customers.Load();

        foreach (var order in db.Orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_PrincipalsLoadedBeforeASingleEntityQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    string Main(MyDbContext db)
    {
        db.Customers.Load();

        var order = db.Orders.First();
        return order.Customer.Name;
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_PrincipalsLoadedInAnEnclosingBlock_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, bool verbose)
    {
        db.Customers.Load();

        if (verbose)
        {
            foreach (var order in db.Orders.ToList())
            {
                Console.WriteLine(order.Customer.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_EveryTableOnAReferencePathLoaded_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        db.Customers.Load();
        db.Addresses.Load();

        foreach (var order in db.Orders.ToList())
        {
            Console.WriteLine(order.Customer.Address.City);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_PrincipalsOfAnIncludedCollectionLoaded_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        db.Products.Load();

        foreach (var order in db.Orders.Include(o => o.Items).ToList())
        {
            foreach (var item in order.Items)
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_OnlyPartOfTheReferencePathLoaded_ReportsTheRest()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        db.Customers.Load();

        foreach (var order in db.Orders.ToList())
        {
            Console.WriteLine({|#0:order.Customer.Address|}.City);
        }
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_DependentsLoadedBeforeACollectionRead_StillReports()
    {
        // Fix-up fills order.Items, but does not mark the collection loaded: a lazy-loading proxy
        // still runs one query per order (measured by the N+1 benchmark's L21).
        await VerifyOriginFlowAsync(
            @"
    int Main(MyDbContext db)
    {
        db.OrderItems.Load();

        var total = 0;
        foreach (var order in db.Orders.ToList())
        {
            total += {|#0:order.Items|}.Count;
        }

        return total;
    }
",
            Diagnostic(0, "Items", "Order")
        );
    }

    [Theory]
    // Only some of the customers are tracked.
    [InlineData("db.Customers.Where(c => c.Rating > 3).Load();", "db.Orders.ToList()")]
    [InlineData("db.Customers.Take(100).Load();", "db.Orders.ToList()")]
    // Nothing is tracked, so there is nothing to fix up.
    [InlineData("db.Customers.AsNoTracking().Load();", "db.Orders.ToList()")]
    [InlineData("db.Customers.Load();", "db.Orders.AsNoTracking().ToList()")]
    [InlineData("db.Customers.Load();", "db.Orders.AsNoTrackingWithIdentityResolution().ToList()")]
    // Another table.
    [InlineData("db.Products.Load();", "db.Orders.ToList()")]
    // Another context.
    [InlineData("other.Customers.Load();", "db.Orders.ToList()")]
    // The tracked customers are thrown away again.
    [InlineData("db.Customers.Load(); db.ChangeTracker.Clear();", "db.Orders.ToList()")]
    // May not have run.
    [InlineData("if (flag) db.Customers.Load();", "db.Orders.ToList()")]
    [InlineData("Action preload = () => db.Customers.Load();", "db.Orders.ToList()")]
    // A list of customers is not followed as a preload: only Load and LoadAsync are.
    [InlineData("var customers = db.Customers.ToList();", "db.Orders.ToList()")]
    public async Task TestCrime_PreloadThatDoesNotCoverTheReference_StillReports(string preload, string query)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, MyDbContext other, bool flag)
    {
        "
                + preload
                + @"

        var orders = "
                + query
                + @";
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_PrincipalsLoadedAfterTheQuery_StillReports()
    {
        // Fix-up would populate the reference here too. Only a preload that runs before the query
        // is followed.
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        var orders = db.Orders.ToList();
        db.Customers.Load();

        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
