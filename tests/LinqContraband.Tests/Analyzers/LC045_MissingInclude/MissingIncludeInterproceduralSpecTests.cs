using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The specification for LC045's interprocedural gap, pinned as current behaviour.
///
/// Reads reached only through a callee are quiet today, by design rather than by oversight: the
/// callee may load the navigation itself, so capturing the collection is treated as an escape.
/// `docs/design/lc045-interprocedural-scope.md` records what closing that would require.
///
/// The closure case was closed in 5.7.47: a callee whose only use of the collection is iterating it
/// and reading navigations is not an escape, so its reads are proven at the call site. The
/// entity-taking case remains open — it needs proof that the callee does not load the navigation.
/// A `TestDeliberate_` case must stay quiet whatever happens, because reporting it would be a false
/// positive; those are the boundary, and they are why this was specified before it was attempted.
///
/// This mirrors 5.7.37, where pinning a gap with its control cases made the fix in 5.7.38 a matter
/// of reading the evidence rather than hunting for it.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_LocalFunctionClosingOverTheCollection_Reports()
    {
        // The narrowest slice, closed: same method, body fully visible, one call site, and the loop
        // is the same loop that reports when written inline. The read is proven at the call, where
        // the collection's state is known, and reported on the read itself.
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
                Console.WriteLine({|#0:order.Customer|}.Name);
            }
        }

        Print();
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_LocalFunctionTakingTheEntity_Reports()
    {
        // The callee receives the entity and only reads a navigation on it, so it cannot be the
        // loading mechanism the read needs. The parameter binds to the argument's origin.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Show(Order order) => Console.WriteLine({|#0:order.Customer|}.Name);

        foreach (var order in orders)
        {
            Show(order);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestDeliberate_EntityCalleeThatPassesTheEntityOn_MustStayQuiet()
    {
        // The callee hands the entity to another method, which could load the navigation.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Show(Order order)
        {
            Hydrate(order);
            Console.WriteLine(order.Customer.Name);
        }

        foreach (var order in orders)
        {
            Show(order);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestDeliberate_EntityCalleeInvokedFromTwoPlaces_MustStayQuiet()
    {
        // Two call sites can hand the callee different entities, so the parameter's origin is
        // ambiguous.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var others = db.Orders.ToList();

        void Show(Order order) => Console.WriteLine(order.Customer.Name);

        foreach (var order in orders)
        {
            Show(order);
        }

        foreach (var order in others)
        {
            Show(order);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_EntityCalleeOverAnIncludedQuery_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();

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

    [Fact]
    public async Task TestDeliberate_LocalFunctionThatAlsoHandsTheCollectionOut_MustStayQuiet()
    {
        // The callee reads the collection but also passes it to a helper that could load the
        // navigation. Only a callee whose sole use is reading may be lifted; anything else stays
        // an escape.
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();

        void Print()
        {
            Hydrate(orders);
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }

        Print();
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }
}
