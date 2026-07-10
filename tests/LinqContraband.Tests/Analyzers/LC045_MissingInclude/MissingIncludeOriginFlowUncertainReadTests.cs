namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_OriginFlow_HelperEscapeBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_OneBranchEscapeBeforeRead_NoDiagnostic()
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
        }

        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ResultStorageBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private List<Order> _orders;

    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        _orders = orders;
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_LambdaCaptureBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Func<List<Order>> capture = () => orders;
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }

        Console.WriteLine(capture);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ResultReassignmentBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders = new List<Order>();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_EntityLocalRepointBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        order = new Order();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_OneBranchEntityRepointBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool repoint)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (repoint)
        {
            order = new Order();
        }

        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_SameOriginNavigationAssignmentBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order.Customer = new Customer();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_BothBranchesAssignNavigationBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrateFirst)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (hydrateFirst)
        {
            order.Customer = new Customer();
        }
        else
        {
            order.Customer = new Customer();
        }

        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_LoopWriteBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool keepGoing)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        while (keepGoing)
        {
            order.Customer = new Customer();
            Console.WriteLine(order.Customer.Name);
            keepGoing = false;
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ReextractAfterResultReassignment_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        orders = new List<Order>();
        order = orders[0];
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ResultEscapeBeforeExtractedEntityRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        Hydrate(orders);
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_StableAliasNavigationWriteSatisfiesOriginal_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        alias.Customer = new Customer();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_StableAliasEscapeMakesOriginalUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        Hydrate(alias);
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CoalescedMaterializerResultIsUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault() ?? new Order();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_HelperWrappedMaterializerResultIsUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = Normalize(db.Orders.FirstOrDefault());
        Console.WriteLine(order.Customer.Name);
    }

    Order Normalize(Order order) => order;
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CapturedLocalFunctionCallBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        void Hydrate() { order.Customer = new Customer(); }
        Hydrate();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_AliasCapturedBeforeMaterializerBinding_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        Order order = new Order();
        var alias = order;
        order = db.Orders.FirstOrDefault();
        Console.WriteLine(alias.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConditionalExternalStoreBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Order _stored;

    void Main(bool escape)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        _stored = escape ? order : new Order();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DeconstructionExternalStoreBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Order _stored;

    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Order other = new Order();
        (_stored, other) = (order, other);
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_LambdaCapturedBeforeMaterializerBinding_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        List<Order> orders = null;
        Func<List<Order>> expose = () => orders;
        orders = db.Orders.ToList();
        Hydrate(expose());
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CapturedLocalFunctionMethodGroupBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        void Hydrate() { order.Customer = new Customer(); }
        Action action = Hydrate;
        action();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_SiblingAliasWriteSatisfiesRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var firstAlias = order;
        var secondAlias = order;
        firstAlias.Customer = new Customer();
        Console.WriteLine(secondAlias.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_SiblingAliasEscapeMakesReadUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var firstAlias = order;
        var secondAlias = order;
        Hydrate(firstAlias);
        Console.WriteLine(secondAlias.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ForeachAliasWriteSatisfiesEntity_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            var alias = order;
            alias.Customer = new Customer();
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ForeachAliasEscapeMakesEntityUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            var alias = order;
            Hydrate(alias);
            Console.WriteLine(order.Customer.Name);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConditionalHelperArgumentBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Hydrate(hydrate ? order : new Order());
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_IndexedEntityRepointBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders[0].Customer = new Customer();
        orders[0] = new Order();
        Console.WriteLine(orders[0].Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_BothBranchesAssignNestedNavigation_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrateFirst)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        if (hydrateFirst)
        {
            order.Customer.Address = new Address();
        }
        else
        {
            order.Customer.Address = new Address();
        }

        Console.WriteLine(order.Customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DirectIndexWritePropagatesToExtractedLocal_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders[0].Customer = new Customer();
        var order = orders[0];
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DirectIndexEscapePropagatesToExtractedLocal_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders[0]);
        var order = orders[0];
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ExtractedLocalWritePropagatesToDirectIndex_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        order.Customer = new Customer();
        Console.WriteLine(orders[0].Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ExtractedLocalEscapePropagatesToDirectIndex_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var order = orders[0];
        Hydrate(order);
        Console.WriteLine(orders[0].Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DeconstructionRepointBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        (order, _) = (new Order(), 0);
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConditionalInstanceCallBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var customer = db.Customers.FirstOrDefault();
        customer?.GetDetached();
        Console.WriteLine(customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConstructorEscapeBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var loader = new Loader(order);
        Console.WriteLine(order.Customer.Name);
        Console.WriteLine(loader);
    }

    sealed class Loader
    {
        public Loader(Order order) { }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CaptureBeforeLaterExtraction_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Action _callback;

    void Main()
    {
        Order order = null;
        _callback = () => Hydrate(order);
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        order = orders[0];
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_FullyIncludedReferenceNavigationLocal_NoDiagnostic()
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
        var customer = order.Customer;
        Console.WriteLine(customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConflictingConditionalOriginsRemainUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useFirst)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var first = orders[0];
        var second = orders[1];
        var selected = useFirst ? first : second;
        Console.WriteLine(selected.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_PrefixLocalWriteSatisfiesDirectNestedRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var customer = order.Customer;
        customer.Address = new Address();
        Console.WriteLine(order.Customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConstructorEscapePrecedesObjectInitializerRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var loader = new Loader(order) { Name = order.Customer.Name };
        Console.WriteLine(loader);
    }

    sealed class Loader
    {
        public Loader(Order order) { }
        public string Name { get; set; }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CollectionRootDeconstructionRepoint_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        (orders, _) = (new List<Order>(), 0);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DeconstructionPreservesTargetOrder_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        var alias = order;
        (order.Customer, order) = (new Customer(), new Order());
        Console.WriteLine(alias.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CompositeYieldReturnBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    IEnumerable<Order> Main(bool expose)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        yield return expose ? order : new Order();
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ChainedExternalStoreBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Order _stored;

    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Order alias;
        _stored = alias = order;
        Console.WriteLine(order.Customer.Name);
        Console.WriteLine(alias);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_RefLocalExternalStoreBeforeRead_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Order _stored;

    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        ref Order slot = ref _stored;
        slot = order;
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_LiveNestedAccessAfterParentRepoint_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        order = new Order();
        Console.WriteLine(order.Customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_OneBranchLiveNestedParentRepoint_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool repoint)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        if (repoint)
        {
            order = new Order();
        }

        Console.WriteLine(order.Customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConflictingExplicitBranchOriginsRemainUncertain_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useFirst)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Order selected;
        if (useFirst)
        {
            selected = orders[0];
        }
        else
        {
            selected = orders[1];
        }

        Console.WriteLine(selected.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_RebindCarriesSourceNavigationWrite_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders[1].Customer = new Customer();
        orders[0] = orders[1];
        Console.WriteLine(orders[0].Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_RebindCarriesSourceEscape_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders[1]);
        orders[0] = orders[1];
        Console.WriteLine(orders[0].Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_RootAliasObservesParentNavigationWrite_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(o => o.Customer).FirstOrDefault();
        var alias = order;
        order.Customer = new Customer();
        Console.WriteLine(alias.Customer.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_AssignmentExpressionCallArgument_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Order alias;
        Hydrate(alias = order);
        Console.WriteLine(order.Customer.Name);
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CoalescingExternalStore_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    private Order _stored;

    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        _stored ??= order;
        Console.WriteLine(order.Customer.Name);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ConflictingNavigationPrefixesAtMerge_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool useBilling)
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(o => o.Customer)
            .Include(o => o.BillingCustomer)
            .FirstOrDefault();
        Customer selected;
        if (useBilling)
        {
            selected = order.BillingCustomer;
        }
        else
        {
            selected = order.Customer;
        }

        Console.WriteLine(selected.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DifferentPrefixRebindCarriesSourceEscape_NoDiagnostic()
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
        Hydrate(order.BillingCustomer);
        selected = order.BillingCustomer;
        Console.WriteLine(selected.Address.City);
    }

    void Hydrate(Customer customer) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DifferentPrefixRebindCarriesSourceWrite_NoDiagnostic()
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
        order.BillingCustomer.Address = new Address();
        selected = order.BillingCustomer;
        Console.WriteLine(selected.Address.City);
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_EscapedParentBeforeNestedIteration_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        Hydrate(order);

        foreach (var item in order.Items)
        {
            Console.WriteLine(item.Product.Name);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_OneBranchParentEscapeBeforeNestedIteration_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (hydrate)
        {
            Hydrate(order);
        }

        foreach (var item in order.Items)
        {
            Console.WriteLine(item.Product.Name);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ParentEscapeAfterNestedBinding_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            Hydrate(order);
            Console.WriteLine(item.Product.Name);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_ParentEscapeBetweenNestedBindings_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Details)
            .FirstOrDefault();
        foreach (var item in order.Items)
        {
            Hydrate(order);
            foreach (var detail in item.Details)
            {
                Console.WriteLine(detail.Product.Name);
            }
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_NestedForeachRebindsAfterEscapedContinue_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(order => order.Items).ToList();
        foreach (var order in orders)
        {
            if (skip)
            {
                Hydrate(order);
                skip = false;
                continue;
            }

            foreach (var item in order.Items)
            {
                Console.WriteLine({|#0:item.Product|}.Name);
            }
        }
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_StableSiblingAliasEscapeAfterNestedBinding_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        var alias = order;
        foreach (var item in order.Items)
        {
            Hydrate(alias);
            Console.WriteLine(item.Product.Name);
        }
    }

    void Hydrate(Order order) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CollectionArgumentEscapeBeforeAndAfterNestedBinding_NoDiagnostic()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool escapeBeforeBinding)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        if (escapeBeforeBinding)
        {
            Hydrate(order.Items);
        }

        foreach (var item in order.Items)
        {
            if (!escapeBeforeBinding)
            {
                Hydrate(order.Items);
            }

            Console.WriteLine(item.Product.Name);
        }
    }

    void Hydrate(List<OrderItem> items) { }
"
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestInnocent_OriginFlow_CollectionReplacementBeforeNestedBinding_NoDiagnostic(
        bool replaceNestedCollection
    )
    {
        var programBody = replaceNestedCollection
            ? @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Details)
            .FirstOrDefault();
        foreach (var item in order.Items)
        {
            item.Details = BuildDetails();
            foreach (var detail in item.Details)
            {
                Console.WriteLine(detail.Product.Name);
            }
        }
    }

    List<OrderItemDetail> BuildDetails() => new List<OrderItemDetail>();
"
            : @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        order.Items = BuildItems();
        foreach (var item in order.Items)
        {
            Console.WriteLine(item.Product.Name);
        }
    }

    List<OrderItem> BuildItems() => new List<OrderItem>();
";

        await VerifyOriginFlowAsync(programBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCrime_OriginFlow_FreshNestedIterationDoesNotCarryPriorItemFacts(
        bool capturesAlias
    )
    {
        var programBody = capturesAlias
            ? @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            var alias = item;
            if (skip)
            {
                Store(() => Hydrate(alias));
                skip = false;
                continue;
            }

            Console.WriteLine({|#0:alias.Product|}.Name);
        }
    }

    void Store(Action action) { }
    void Hydrate(OrderItem item) { }
"
            : @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            if (skip)
            {
                item.Product = new Product();
                skip = false;
                continue;
            }

            var alias = item;
            Console.WriteLine({|#0:alias.Product|}.Name);
        }
    }
";

        await VerifyOriginFlowAsync(programBody, Diagnostic(0, "Items.Product", "Order"));
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CapturedLoopExternalAliasRemainsUncertain()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        OrderItem alias = null;
        foreach (var item in order.Items)
        {
            if (skip)
            {
                alias = item;
                Store(() => Hydrate(alias));
                skip = false;
                continue;
            }

            alias = item;
            Console.WriteLine(alias.Product.Name);
        }
    }

    void Store(Action action) { }
    void Hydrate(OrderItem item) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_CollectionReplacementSatisfiesCollectionPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        order.Items = BuildItems();
        Console.WriteLine(order.Items.Count);
    }

    List<OrderItem> BuildItems() => new List<OrderItem>();
"
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_MultipleScopedCollectionArgumentsKeepSiblingPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders
            .Include(order => order.Items)
            .Include(order => order.OtherItems)
            .FirstOrDefault();
        Hydrate(order.Items, order.OtherItems);
        Console.WriteLine({|#0:order.Customer|}.Name);
    }

    void Hydrate(List<OrderItem> first, List<OrderItem> second) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_ReadBeforeCollectionEscapeAndSiblingPath_StillReport()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            Console.WriteLine({|#0:item.Product|}.Name);
            Hydrate(order.Items);
            Console.WriteLine(item.Product.Name);
        }

        Console.WriteLine({|#1:order.Customer|}.Name);
    }

    void Hydrate(List<OrderItem> items) { }
",
            Diagnostic(0, "Items.Product", "Order"),
            Diagnostic(1, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_UnrelatedCollectionEffectsDoNotSuppressNestedPath()
    {
        await VerifyOriginFlowAsync(
            @"
    void OtherCollectionArgument()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        var other = db.Orders.Include(order => order.Items).FirstOrDefault();
        Hydrate(other.Items);
        foreach (var item in order.Items)
        {
            Console.WriteLine({|#0:item.Product|}.Name);
        }
    }

    void OtherCollectionReplacement()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        var other = db.Orders.Include(order => order.Items).FirstOrDefault();
        other.Items = BuildItems();
        foreach (var item in order.Items)
        {
            Console.WriteLine({|#1:item.Product|}.Name);
        }
    }

    void Hydrate(List<OrderItem> items) { }
    List<OrderItem> BuildItems() => new List<OrderItem>();
",
            Diagnostic(0, "Items.Product", "Order"),
            Diagnostic(1, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_RepointedSiblingAliasDoesNotSuppressNestedIteration()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        var alias = order;
        alias = new Order();
        foreach (var item in order.Items)
        {
            Hydrate(alias);
            Console.WriteLine({|#0:item.Product|}.Name);
        }
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_CollectionReplacementAfterNestedBindingKeepsDetachedItem()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            order.Items = BuildItems();
            Console.WriteLine({|#0:item.Product|}.Name);
        }
    }

    List<OrderItem> BuildItems() => new List<OrderItem>();
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_OriginFlow_TransitiveFreshBodyAliasDoesNotCarryCapture()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            var first = item;
            var second = first;
            if (skip)
            {
                Store(() => Hydrate(second));
                skip = false;
                continue;
            }

            Console.WriteLine({|#0:second.Product|}.Name);
        }
    }

    void Store(Action action) { }
    void Hydrate(OrderItem item) { }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_OuterBodyAliasCapturedInInnerLoopRemainsUncertain()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main(bool skip)
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(order => order.Items).ToList();
        foreach (var order in orders)
        {
            OrderItem alias = null;
            foreach (var item in order.Items)
            {
                if (skip)
                {
                    alias = item;
                    Store(() => Hydrate(alias));
                    skip = false;
                    continue;
                }

                alias = item;
                Console.WriteLine(alias.Product.Name);
            }
        }
    }

    void Store(Action action) { }
    void Hydrate(OrderItem item) { }
"
            );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_BodyLocalReassignmentDoesNotClearCapture()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        foreach (var item in order.Items)
        {
            var alias = item;
            Store(() => Hydrate(alias));
            alias = item;
            Console.WriteLine(alias.Product.Name);
        }
    }

    void Store(Action action) { }
    void Hydrate(OrderItem item) { }
"
        );
    }

    [Fact]
    public async Task TestInnocent_OriginFlow_DeconstructionCollectionReplacementSatisfiesAndInvalidates()
    {
        await VerifyOriginFlowAsync(
            @"
    void DirectCollectionRead()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        int ignored;
        (order.Items, ignored) = (BuildItems(), 0);
        Console.WriteLine(order.Items.Count);
    }

    void DescendantProvenance()
    {
        var db = new MyDbContext();
        var order = db.Orders.Include(order => order.Items).FirstOrDefault();
        int ignored;
        (order.Items, ignored) = (BuildItems(), 0);
        foreach (var item in order.Items)
        {
            Console.WriteLine(item.Product.Name);
        }
    }

    List<OrderItem> BuildItems() => new List<OrderItem>();
"
        );
    }
}
