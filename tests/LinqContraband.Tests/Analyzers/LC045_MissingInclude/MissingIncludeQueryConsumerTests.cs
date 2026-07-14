using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    private const string QueryConsumerMockNamespace =
        @"
namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(
            this DbSet<TEntity> source,
            string sql,
            params object[] parameters) where TEntity : class => source;

        public static IQueryable<TEntity> FromSqlInterpolated<TEntity>(
            this DbSet<TEntity> source,
            FormattableString sql) where TEntity : class => source;

        public static IQueryable<TEntity> FromSql<TEntity>(
            this DbSet<TEntity> source,
            FormattableString sql) where TEntity : class => source;
    }
}
";

    private static Task VerifyQueryConsumerAsync(
        string programBody,
        params Microsoft.CodeAnalysis.Testing.DiagnosticResult[] expected
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
            + MockNamespace
            + QueryConsumerMockNamespace;

        return VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Theory]
    [InlineData("System.Linq.Queryable.Where(predicate: o => o.Id > 0, source: db.Orders)")]
    [InlineData(
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSqlRaw(parameters: new object[0], sql: \"select * from Orders\", source: db.Orders)"
    )]
    public async Task TestCrime_StaticQuerySourceNamedOutOfOrder_Reports(string query)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = "
                + query
                + @".ToList();
        foreach (var order in orders)
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.AsQueryable()")]
    [InlineData("db.Orders.IgnoreAutoIncludes()")]
    [InlineData("db.Orders.FromSql($\"select * from Orders where Id = {1}\")")]
    [InlineData("db.Orders.FromSqlInterpolated($\"select * from Orders where Id = {1}\")")]
    public async Task TestCrime_ExactQueryPreservingStep_Reports(string query)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = "
                + query
                + @".ToList();
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
    public async Task TestInnocent_QueryPreservingStepWithIncludedNavigation_NoDiagnostic()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.IgnoreAutoIncludes().Include(o => o.Customer).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_ToHashSetMaterializer_Reports()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToHashSet();
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
    public async Task TestCrime_QueryRootElementAtMaterializer_Reports()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.ElementAt(0);
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestCrime_ReorderedStaticQueryRootElementAt_Reports()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = System.Linq.Queryable.ElementAt(index: 0, source: db.Orders);
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("await db.Orders.ToHashSetAsync()", true)]
    [InlineData("await db.Orders.ToHashSetAsync(EqualityComparer<Order>.Default)", true)]
    [InlineData("await db.Orders.ElementAtAsync(0)", false)]
    [InlineData("await db.Orders.ElementAtOrDefaultAsync(0)", false)]
    [InlineData("db.Orders.ElementAtOrDefault(0)", false)]
    public async Task TestCrime_ExactAsyncMaterializerVariants_Report(
        string materializer,
        bool collection
    )
    {
        var assignment = collection
            ? "var orders = " + materializer + ";"
            : "var order = " + materializer + ";";
        var use = collection
            ? "foreach (var order in orders) { Console.WriteLine({|#0:order.Customer|}.Name); }"
            : "Console.WriteLine({|#0:order.Customer|}.Name);";
        await VerifyQueryConsumerAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        "
                + assignment
                + @"
        "
                + use
                + @"
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_FrameworkPrefixedLookalikeStaysQuiet()
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = System.Linq.QueryableLookalike.Where(source: db.Orders, predicate: order => order.Id > 0).ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace
            + @"
namespace System.Linq
{
    public static class QueryableLookalike
    {
        public static IQueryable<T> Where<T>(IQueryable<T> source, Func<T, bool> predicate) => source;
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    [InlineData("orders.ForEach(order => Console.WriteLine({|#0:order.Customer|}.Name));")]
    [InlineData(
        "Console.WriteLine(orders.Where(order => {|#0:order.Customer|}.Name == \"vip\").Any());"
    )]
    [InlineData("var names = orders.Select(order => {|#0:order.Customer|}.Name).ToList();")]
    [InlineData("Console.WriteLine(orders.Any(order => {|#0:order.Customer|}.Name == \"vip\"));")]
    [InlineData("Console.WriteLine(orders.All(order => {|#0:order.Customer|}.Name == \"vip\"));")]
    public async Task TestCrime_ExactMaterializedCollectionConsumerCallback_Reports(string consume)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        "
                + consume
                + @"
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData(
        "orders.Where(first => first.Id > 0).Any(order => {|#0:order.Customer|}.Name == \"vip\")"
    )]
    [InlineData(
        "System.Linq.Enumerable.Any(predicate: order => {|#0:order.Customer|}.Name == \"vip\", source: orders)"
    )]
    public async Task TestCrime_WhereProvenanceAndReorderedStaticAny_Report(string consume)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine("
                + consume
                + @");
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData(
        "db.Orders.ToList().ForEach(order => Console.WriteLine({|#0:order.Customer|}.Name));"
    )]
    [InlineData(
        "Console.WriteLine(db.Orders.ToList().Any(order => {|#0:order.Customer|}.Name == \"vip\"));"
    )]
    public async Task TestCrime_DirectInlineMaterializedCollectionConsumer_Reports(string consume)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        "
                + consume
                + @"
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData(
        "db.Orders.ToList().Where(first => first.Id > 0).Any(order => {|#0:order.Customer|}.Name == \"vip\")"
    )]
    [InlineData(
        "System.Linq.Enumerable.Any(predicate: order => {|#0:order.Customer|}.Name == \"vip\", source: db.Orders.ToList())"
    )]
    public async Task TestCrime_InlineWhereAndReorderedStaticAny_Report(string consume)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        Console.WriteLine("
                + consume
                + @");
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.ToHashSet(EqualityComparer<Order>.Default)", true)]
    [InlineData(
        "await db.Orders.ToHashSetAsync(default(System.Threading.CancellationToken))",
        true
    )]
    [InlineData(
        "await db.Orders.ToHashSetAsync(EqualityComparer<Order>.Default, default(System.Threading.CancellationToken))",
        true
    )]
    [InlineData(
        "await db.Orders.ElementAtOrDefaultAsync(0, default(System.Threading.CancellationToken))",
        false
    )]
    public async Task TestCrime_ExplicitSupportedMaterializerOverloads_Report(
        string materializer,
        bool collection
    )
    {
        var assignment = collection
            ? "var orders = " + materializer + ";"
            : "var order = " + materializer + ";";
        var use = collection
            ? "foreach (var order in orders) { Console.WriteLine({|#0:order.Customer|}.Name); }"
            : "Console.WriteLine({|#0:order.Customer|}.Name);";

        await VerifyQueryConsumerAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        "
                + assignment
                + @"
        "
                + use
                + @"
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.FirstOrDefault(order => order.Id == 1)")]
    [InlineData(
        "await db.Orders.FirstOrDefaultAsync(order => order.Id == 1, default(System.Threading.CancellationToken))"
    )]
    public async Task TestCrime_QueryRootPredicateMaterializerOverloads_Report(string materializer)
    {
        await VerifyQueryConsumerAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        var order = "
                + materializer
                + @";
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData("db.Orders.FirstOrDefault(fallback)")]
    [InlineData("db.Orders.FirstOrDefault(order => order.Id == 1, fallback)")]
    public async Task TestCrime_QueryRootDefaultValueMaterializerOverloads_Report(
        string materializer
    )
    {
        var test =
            Usings
            + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var fallback = new Order();
        var order = "
            + materializer
            + @";
        Console.WriteLine({|#0:order.Customer|}.Name);
    }
}
"
            + MockNamespace
            + QueryConsumerMockNamespace;

        var analyzerTest = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier
        >
        {
            TestCode = test,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80,
        };
        analyzerTest.ExpectedDiagnostics.Add(Diagnostic(0, "Customer", "Order"));
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task TestInnocent_FrameworkNamespaceMaterializerLookalikesStayQuiet()
    {
        var test =
            Usings
            + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var orders = System.Linq.MaterializerLookalike.ToList(db.Orders);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }

        var first = await Microsoft.EntityFrameworkCore.AsyncMaterializerLookalike.FirstAsync(db.Orders);
        Console.WriteLine(first.Customer.Name);
    }
}
"
            + MockNamespace
            + @"
namespace System.Linq
{
    public static class MaterializerLookalike
    {
        public static List<T> ToList<T>(IQueryable<T> source) => new List<T>();
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class AsyncMaterializerLookalike
    {
        public static Task<T> FirstAsync<T>(IQueryable<T> source) => null;
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    [InlineData("var ids = orders.Select(order => order.Id).ToList();")]
    [InlineData("var ids = orders.Select(order => { return order.Id; }).ToList();")]
    public async Task TestCrime_ScalarSelectDoesNotPoisonLaterOrdinaryRead(string select)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        "
                + select
                + @"
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
    public async Task TestInnocent_CallbackOnUnrelatedCollectionDoesNotBorrowProvenance()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var unrelated = new List<Order>();
        Console.WriteLine(unrelated.Any(order => order.Customer.Name == ""vip""));
    }
"
        );
    }

    [Theory]
    [InlineData("var aliases = orders.Select(order => order).ToList();")]
    [InlineData("var aliases = orders.Select(order => (object)order).ToList();")]
    public async Task TestInnocent_EntityReturningSelectKeepsLaterReadsConservative(string select)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        "
                + select
                + @"
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CallbackBeforeMaterializerAssignmentStaysQuiet()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        List<Order> orders = new();
        orders.ForEach(order => Console.WriteLine(order.Customer.Name));

        var db = new MyDbContext();
        orders = db.Orders.ToList();
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CallbackAfterResultReassignmentStaysQuiet()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders = new List<Order>();
        orders.ForEach(order => Console.WriteLine(order.Customer.Name));
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_CallbackAfterCollectionEscapeStaysQuiet()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Hydrate(orders);
        orders.ForEach(order => Console.WriteLine(order.Customer.Name));
    }

    void Hydrate(List<Order> orders) { }
"
        );
    }

    [Fact]
    public async Task TestCrime_CallbackBeforeLaterCollectionEscapeStillReports()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders.ForEach(order => Console.WriteLine({|#0:order.Customer|}.Name));
        Hydrate(orders);
    }

    void Hydrate(List<Order> orders) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_EffectfulWhereDoesNotForwardCallbackProvenance()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine(orders
            .Where(order => Hydrate(order))
            .Any(order => order.Customer.Name == ""vip""));
    }

    bool Hydrate(Order order) => true;
"
        );
    }

    [Fact]
    public async Task TestCrime_PropertyPatternNavigationRead_ReportsMaximalPath()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var order = db.Orders.FirstOrDefault();
        if (order is { {|#0:Customer.Address|}.City: not null })
        {
            Console.WriteLine(order.Id);
        }
    }
",
            Diagnostic(0, "Customer.Address", "Order")
        );
    }

    [Fact]
    public async Task TestInnocent_IncludedPropertyPatternAndScalarCallbackStayPrecise()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Customer).ToList();
        Console.WriteLine(orders.Any(order => order.Id > 0));
        foreach (var order in orders)
        {
            if (order is { Customer.Name: not null })
            {
                Console.WriteLine(order.Customer.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task TestCrime_ScalarAnyDoesNotPoisonLaterOrdinaryRead()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        Console.WriteLine(orders.Any(order => order.Id > 0));
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
    public async Task TestCrime_CallbackReadBeforeEscapeStillReportsButReadAfterStaysConservative()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders.ForEach(order =>
        {
            Console.WriteLine({|#0:order.Customer|}.Name);
            Hydrate(order);
            Console.WriteLine(order.Customer.Name);
        });
    }

    void Hydrate(Order order) { }
",
            Diagnostic(0, "Customer", "Order")
        );
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TestCrime_CallbackOneBranchWriteRequiresAllPaths(
        bool bothBranches,
        bool reports
    )
    {
        var write = bothBranches
            ? "if (hydrate) order.Customer = new Customer(); else order.Customer = new Customer();"
            : "if (hydrate) order.Customer = new Customer();";
        var expected = reports
            ? new[] { Diagnostic(0, "Customer", "Order") }
            : System.Array.Empty<Microsoft.CodeAnalysis.Testing.DiagnosticResult>();
        await VerifyQueryConsumerAsync(
            @"
    void Main(bool hydrate)
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        orders.Any(order =>
        {
            "
                + write
                + @"
            return {|#0:order.Customer|}.Name == ""vip"";
        });
    }
",
            expected
        );
    }

    [Fact]
    public async Task TestInnocent_LookalikeAndMultiSourceCallbacksStayQuiet()
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        CustomOperators.Where(source: orders, predicate: order => Console.WriteLine(order.Customer.Name));
        var others = new List<Order>();
        var joined = orders.Join(others, left => left.Id, right => right.Id, (left, right) => left.Customer.Name);
        Console.WriteLine(joined.Count());
    }

    private static class CustomOperators
    {
        public static void Where<T>(IEnumerable<T> source, Action<T> predicate) { }
    }
"
        );
    }
}
