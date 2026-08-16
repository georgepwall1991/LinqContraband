using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// An element-preserving view of a navigation collection yields the very instances the
/// navigation holds, so `foreach (var item in order.Items.Where(...))` is the same nested read as
/// `foreach (var item in order.Items)`. Only the direct property reference was recognised before.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("order.Items.Where(i => i.Id > 0)")]
    [InlineData("order.Items.OrderBy(i => i.Id)")]
    [InlineData("order.Items.OrderByDescending(i => i.Id)")]
    [InlineData("order.Items.Skip(1)")]
    [InlineData("order.Items.Take(5)")]
    [InlineData("order.Items.Distinct()")]
    [InlineData("order.Items.AsEnumerable()")]
    [InlineData("order.Items.Where(i => i.Id > 0).OrderBy(i => i.Sku).Take(3)")]
    [InlineData("order.Items.OrderBy(i => i.Id).ThenBy(i => i.Sku)")]
    public async Task TestCrime_ForeachOverNavigationCollectionView_ReportsNestedPath(string view)
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            foreach (var item in "
                + view
                + @")
            {
                Console.WriteLine({|#0:item.Product|}.Name);
            }
        }
    }
",
            Diagnostic(0, "Items.Product", "Order")
        );
    }

    // A navigation read inside the view's own predicate — `order.Items.Where(i => i.Product...)`
    // — is NOT reported: the callback machinery binds its parameter to the materialized root
    // collection, not to a navigation-prefixed origin. That gap is recorded in the
    // analyzer-health candidate queue rather than half-implemented here.
    [Fact]
    public async Task TestInnocent_NavigationCollectionViewOnIncludedPath_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items.Where(i => i.Id > 0))
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionViewWithEffectfulPredicate_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items.Where(i => Load(i)))
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }

    bool Load(OrderItem item) => true;
"
        );
    }

    [Fact]
    public async Task TestInnocent_NavigationCollectionProjection_StaysQuiet()
    {
        await VerifyOriginFlowAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in orders)
        {
            foreach (var item in order.Items.Select(i => Rewrap(i)))
            {
                Console.WriteLine(item.Product.Name);
            }
        }
    }

    OrderItem Rewrap(OrderItem item) => item;
"
        );
    }
}
