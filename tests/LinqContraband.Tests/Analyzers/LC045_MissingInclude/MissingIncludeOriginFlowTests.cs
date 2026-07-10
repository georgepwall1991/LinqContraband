using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    private static Task VerifyOriginFlowAsync(
        string programBody,
        params DiagnosticResult[] expected
    )
    {
        var test =
            Usings
            + @"
class Program
{
"
            + programBody
            + @"
}
"
            + MockNamespace;

        return VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeHelperEscape_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        Hydrate(orders);
    }

    void Hydrate(List<Order> orders) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeResultReturn_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    List<Order> Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        return orders;
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeResultStorage_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    private List<Order> _orders;

    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        _orders = orders;
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeLambdaCapture_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        Func<List<Order>> capture = () => orders;
        Console.WriteLine(capture);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeResultReassignment_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }

        orders = new List<Order>();
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeEntityLocalRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        Console.WriteLine({|#0:order.Customer|}.Name);
        order = new Order();
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_OneBranchNavigationAssignment_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (hydrate)
        {
            order.Customer = new Customer();
        }

        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_WriteOnDifferentEntity_DoesNotSatisfyRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var second = orders[1];
        first.Customer = new Customer();
        Console.WriteLine({|#0:second.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_EscapeOfDifferentEntity_DoesNotSuppressRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var second = orders[1];
        Hydrate(first);
        Console.WriteLine({|#0:second.Customer|}.Name);
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_LoopReadBeforeWrite_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool keepGoing)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        while (keepGoing)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
            order.Customer = new Customer();
            keepGoing = false;
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ExtractedEntitySurvivesResultReassignment_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        orders = new List<Order>();
        Console.WriteLine({|#0:first.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ForeachEntitySurvivesResultReassignment_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            orders = new List<Order>();
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_SameSpellingIndexedSitesRemainDistinct_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var index = 0;
        orders[index].Customer = new Customer();
        index = 1;
        Console.WriteLine({|#0:orders[index].Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_EscapeOnTerminatingBranchDoesNotSuppressRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        if (hydrate)
        {
            Hydrate(orders);
            return;
        }

        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ForeachRebindsAfterEscapedContinue_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            if (skip)
            {
                Hydrate(order);
                skip = false;
                continue;
            }

            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_DeconstructionRhsReadPrecedesNavigationWrite()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Customer oldCustomer;
        (order.Customer, oldCustomer) = (new Customer(), {|#0:order.Customer|});
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeCapturedLocalFunctionCall_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        void Hydrate() { order.Customer = new Customer(); }
        Console.WriteLine({|#0:order.Customer|}.Name);
        Hydrate();
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_StableAliasSurvivesSourceRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        order = new Order();
        Console.WriteLine({|#0:alias.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_IndexedAliasSurvivesSourceRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var alias = first;
        first = new Order();
        Console.WriteLine({|#0:alias.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_AliasIgnoresWriteAfterSourceRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        order = new Order();
        order.Customer = new Customer();
        Console.WriteLine({|#0:alias.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_AliasIgnoresEscapeAfterSourceRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        order = new Order();
        Hydrate(order);
        Console.WriteLine({|#0:alias.Customer|}.Name);
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ConfigureAwaitWrappedMaterializer_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        var orders = await db.Orders.ToListAsync().ConfigureAwait(false);
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
    public async Task TestCrime_OriginFlow_OldAliasWriteDoesNotSatisfyReboundSource()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var alias = first;
        first = orders[1];
        alias.Customer = new Customer();
        Console.WriteLine({|#0:first.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_OldAliasEscapeDoesNotSuppressReboundSource()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var alias = first;
        first = orders[1];
        Hydrate(alias);
        Console.WriteLine({|#0:first.Customer|}.Name);
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReboundIndexedEntityDoesNotKeepSatisfiedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders[0].Customer = new Customer();
        orders[0] = orders[1];
        Console.WriteLine({|#0:orders[0].Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_IdenticalIndexedBindingsAcrossBranchesStillReport()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useFirstBranch)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Order selected;
        if (useFirstBranch)
        {
            selected = orders[0];
        }
        else
        {
            selected = orders[0];
        }

        Console.WriteLine({|#0:selected.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_SeparateMaterializerWriteDoesNotSatisfyRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var first = db.Orders.FirstOrDefault();
        var second = db.Orders.FirstOrDefault();
        first.Customer = new Customer();
        Console.WriteLine({|#0:second.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_DifferentEntityTypeWriteDoesNotSatisfyRead()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var customer = db.Customers.FirstOrDefault();
        customer.Address = new Address();
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_OneBranchNestedNavigationAssignment_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        if (hydrate)
        {
            order.Customer.Address = new Address();
        }

        Console.WriteLine({|#0:order.Customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReferenceNavigationLocalPreservesPathPrefix()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var customer = order.Customer;
        Console.WriteLine({|#0:customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_IdenticalAliasBindingsAcrossBranchesStillReport()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useFirstBranch)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Order alias;
        if (useFirstBranch)
        {
            alias = order;
        }
        else
        {
            alias = order;
        }

        Console.WriteLine({|#0:alias.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_IdenticalConditionalAliasBindingStillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useFirstBranch)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = useFirstBranch ? order : order;
        Console.WriteLine({|#0:alias.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_DeconstructionRebindToIndexedEntityStillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        (order, _) = (orders[1], 0);
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeConditionalInstanceCall_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var customer = db.Customers.FirstOrDefault();
        Console.WriteLine({|#0:customer.Address|}.City);
        customer?.GetDetached();
    }
",
            Diagnostic(0, "Address", "Customer")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ConstructorLaterArgumentRead_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var loader = new Loader(order, {|#0:order.Customer|}.Name);
        Console.WriteLine(loader);
    }

    sealed class Loader
    {
        public Loader(Order order, string name) { }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_CaptureOnTerminatingBranchDoesNotSuppressRead()
    {
        await VerifyOriginFlowAsync(
            @"
    private Action _callback;

    void Main(bool capture)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (capture)
        {
            _callback = () => Hydrate(order);
            return;
        }

        Console.WriteLine({|#0:order.Customer|}.Name);
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_PrefixEscapeDoesNotSuppressSiblingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var customer = order.Customer;
        Hydrate(customer);
        Console.WriteLine(customer.Address.City);
        Console.WriteLine({|#0:order.Items|}.Count);
    }

    void Hydrate(Customer customer) { }
",
            Diagnostic(0, "Items", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_PrefixExternalStoreDoesNotSuppressSiblingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    private Customer _stored;

    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        _stored = order.Customer;
        Console.WriteLine(order.Customer.Address.City);
        Console.WriteLine({|#0:order.Items|}.Count);
    }
",
            Diagnostic(0, "Items", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_DeconstructionUsesAtomicRhsBindings()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var current = orders[0];
        Order previous;
        (current, previous) = (orders[1], current);
        current.Customer = new Customer();
        Console.WriteLine({|#0:previous.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ParentLocalRebindDropsNestedSatisfiedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var order = orders[0];
        order.Customer.Address = new Address();
        order = orders[1];
        Console.WriteLine({|#0:order.Customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_DirectIndexRebindDropsNestedSatisfiedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        orders[0].Customer.Address = new Address();
        orders[0] = orders[1];
        Console.WriteLine({|#0:orders[0].Customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_NavigationLocalReadBeforeRepoint_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var customer = order.Customer;
        Console.WriteLine({|#0:customer.Address|}.City);
        customer = new Customer();
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ParentWriteDoesNotSatisfyDetachedNavigationAlias()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var customer = order.Customer;
        order.Customer = new Customer();
        Console.WriteLine({|#0:customer.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_AncestorWriteDoesNotSatisfyDeeperDetachedAlias()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(o => o.Customer)
            .ThenInclude(c => c.Address)
            .FirstOrDefault();
        var address = order.Customer.Address;
        order.Customer = new Customer();
        Console.WriteLine({|#0:address.Region|}.Name);
    }
",
            Diagnostic(0, "Customer.Address.Region", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_NavigationLocalRebindToKnownParentStillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        var selected = orders[0].Customer;
        selected = orders[1].Customer;
        Console.WriteLine({|#0:selected.Address|}.City);
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_NavigationLocalRebindToDifferentPrefixReportsCurrentPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(o => o.Customer)
            .Include(o => o.BillingCustomer)
            .FirstOrDefault();
        var selected = order.Customer;
        selected = order.BillingCustomer;
        Console.WriteLine({|#0:selected.Address|}.City);
    }
",
            Diagnostic(0, "BillingCustomer.Address", "Order")
        );
    }
}
