using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Leftover wraps between a whole-table <c>Load</c> and the set that the shipped
/// preload fixtures do not isolate. Dropping <c>AsSplitQuery</c>, <c>AsSingleQuery</c>,
/// <c>OrderByDescending</c>, <c>TagWith</c>, or <c>TagWithCallSite</c> from the
/// preload walk FNs only that pin; the shipped <c>Include.Load</c> and
/// <c>OrderBy.AsTracking.Load</c> cases stay green. <c>AsTracking</c> and
/// <c>OrderBy</c> stay on the shipped composed fixture. <c>ThenBy</c>/<c>ThenByDescending</c>
/// stay unpinned because they require <c>OrderBy</c>. <c>ThenInclude</c> stays
/// unpinned because it is reached through <c>Include</c>. A leftover lookalike
/// wrap still reports.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("db.Customers.AsSplitQuery().Load();")]
    [InlineData("db.Customers.AsSingleQuery().Load();")]
    [InlineData("db.Customers.OrderByDescending(c => c.Id).Load();")]
    [InlineData("db.Customers.TagWith(\"preload\").Load();")]
    [InlineData("db.Customers.TagWithCallSite().Load();")]
    public async Task LeftoverPreloadWrap_StaysQuiet(string preload)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        "
                + preload
                + @"

        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task ConfigureAwait_OnLoadAsync_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    async Task Main()
    {
        var db = new MyDbContext();
        await db.Customers.LoadAsync().ConfigureAwait(false);

        var orders = await db.Orders.ToListAsync();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task PropertyOfThis_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    MyDbContext Db { get; } = new MyDbContext();

    void Main()
    {
        Db.Customers.Load();

        var orders = Db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
"
        );
    }

    [Fact]
    public async Task ContextAsThis_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    class Worker : MyDbContext
    {
        void Run()
        {
            Customers.Load();

            var orders = Orders.ToList();
            foreach (var order in orders)
            {
                Console.WriteLine(order.Customer.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task LookalikeShuffle_OnPreload_StillReports()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        db.Customers.Shuffle().Load();

        var orders = db.Orders.ToList();
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
