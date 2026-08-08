using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// A false-positive sweep over the reporting surfaces added in 5.7.11 through 5.7.17 — in-memory
/// views, aggregate and grouping callbacks, and element extraction. Each case below was checked
/// by hand against what the rule claims, and pinned so that widening the rule again cannot
/// quietly start reporting them.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_Sweep_ScalarCallbacksAndUnprovenSources_StayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, List<Order> other, bool flag)
    {
        var orders = db.Orders.ToList();

        // A callback that never touches a navigation.
        var scalarCount = orders.Count(o => o.Status != null);

        // A scalar projection consumed by an aggregate.
        var idTotal = orders.Select(o => o.Id).Sum();

        // A nested lambda over an unrelated collection.
        var paired = orders.Count(o => other.Any(x => x.Id == o.Id));

        // An owned type has no DbSet, so it is not a navigation.
        var owned = orders.Sum(o => o.Summary.Total);

        // A grouping key that is not a navigation.
        foreach (var group in orders.GroupBy(o => o.Status))
        {
            Console.WriteLine(group.Key);
        }

        // A source the analyzer cannot prove is the materialized collection.
        var chosen = flag ? orders : other;
        foreach (var order in chosen.Where(o => o.Id > 0))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_Sweep_IncludedNavigationAcrossEveryNewSurface_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        var included = db.Orders.Include(o => o.Customer).ToList();

        var total = included.Sum(o => o.Customer.Id);
        var keyed = included.ToDictionary(o => o.Customer.Name);
        var picked = included.First(o => o.Id == 1);
        Console.WriteLine(picked.Customer.Name);

        foreach (var order in included.OrderBy(o => o.Customer.Name))
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_Sweep_ReassignedOrEscapedCollection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, List<Order> other)
    {
        var reassigned = db.Orders.ToList();
        reassigned = other;
        var picked = reassigned.First(o => o.Id == 1);
        Console.WriteLine(picked.Customer.Name);

        var escaped = db.Orders.ToList();
        var view = escaped.Where(o => o.Id > 0);
        Handle(escaped);
        foreach (var order in view)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Handle(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_Sweep_ViewAndExtractionThroughALocalFunction_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        var orders = db.Orders.ToList();

        IEnumerable<Order> View() => orders.Where(o => o.Id > 0);

        foreach (var order in View())
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_Sweep_ChainedViewsAndRepeatedAggregates_ReportOncePerPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db)
    {
        var orders = db.Orders.ToList();
        foreach (var order in orders.Where(o => o.Id > 0).Where(o => o.Status != null))
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    // The three cases below pin the CURRENT boundary of navigation-write suppression: a write is
    // credited to the origin it was made on, not to the collection. A write in the same scope as
    // the read suppresses; a write made in an earlier loop does not reach a later read through a
    // different origin. That is a known false positive for manual relationship fix-up, recorded
    // in the analyzer-health candidate queue. These tests exist so that closing it is a
    // deliberate, visible change rather than an accident.
    [Fact]
    public async Task TestInnocent_NavigationWriteInSameScopeAsRead_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, Customer customer)
    {
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_NavigationWriteInAnEarlierLoop_StillReportsAtALaterLoop()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, Customer customer)
    {
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

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
    public async Task TestCrime_NavigationWriteInAnEarlierLoop_StillReportsAtAnAggregate()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(MyDbContext db, Customer customer)
    {
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            order.Customer = customer;
        }

        var total = orders.Sum(o => {|#0:o.Customer|}.Id);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
