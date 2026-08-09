using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The specification for LC045's interprocedural gap, pinned as current behaviour.
///
/// Reads reached only through a callee are quiet today, by design rather than by oversight: the
/// callee may load the navigation itself, so capturing the collection is treated as an escape.
/// `docs/design/lc045-interprocedural-scope.md` records what closing that would require.
///
/// These tests exist so the work is specified before it is attempted. A `TestFutureGap_` case is
/// one an implementation should make report — flipping it is the point. A `TestDeliberate_` case
/// must stay quiet whatever happens, because reporting it would be a false positive; those are the
/// boundary the implementation has to respect, and they are the reason this is not a small change.
///
/// This mirrors 5.7.37, where pinning a gap with its control cases made the fix in 5.7.38 a matter
/// of reading the evidence rather than hunting for it.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestFutureGap_LocalFunctionClosingOverTheCollection_StaysQuiet()
    {
        // The narrowest slice: same method, body fully visible, one call site, and the loop is the
        // same loop that reports when written inline.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Print()
        {
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }

        Print();
    }
"
        );
    }

    [Fact]
    public async Task TestFutureGap_LocalFunctionTakingTheEntity_StaysQuiet()
    {
        // Harder than the closure case: the callee receives the entity, so an implementation must
        // prove the body does not load the navigation before reporting.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Show(Order order) => Console.WriteLine(order.Customer.Name);

        foreach (var order in orders)
        {
            Show(order);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_LocalFunctionThatExplicitlyLoads_MustStayQuiet()
    {
        // Reporting this would be a false positive: the callee loads the navigation before reading
        // it, which LC045 has recognised as a loading mechanism since 5.7.36. Any implementation of
        // the case above has to keep this one silent.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Show(Order order)
        {
            db.Entry(order).Reference(o => o.Customer).Load();
            Console.WriteLine(order.Customer.Name);
        }

        foreach (var order in orders)
        {
            Show(order);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_LocalFunctionInvokedTwice_MustStayQuiet()
    {
        // Two call sites make the read position ambiguous: the collection's state can differ at
        // each, so attributing the read to one of them would be a guess.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Print()
        {
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }

        Print();
        Hydrate(orders);
        Print();
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_LocalFunctionInvokedAfterAnEscape_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Print()
        {
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }

        Hydrate(orders);
        Print();
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_LocalFunctionOverAnIncludedQuery_MustStayQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();

        void Print()
        {
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }

        Print();
    }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_DelegateVariableConsumer_MustStayQuiet()
    {
        // A delegate can be reassigned or invoked anywhere, so its body is not the callee's body in
        // the way a local function's is. This stays out of scope even for the slice above.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        Action<Order> show = order => Console.WriteLine(order.Customer.Name);

        foreach (var order in orders)
        {
            show(order);
        }
    }
"
        );
    }
}
