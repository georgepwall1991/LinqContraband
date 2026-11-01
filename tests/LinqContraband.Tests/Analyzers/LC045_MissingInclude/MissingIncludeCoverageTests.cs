using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Leftover LC045 query-preserving and query-root materializer names the shipped
/// fixtures do not isolate. Dropping <c>IgnoreQueryFilters</c> from
/// <c>ShapePreservingEntityFrameworkOperators</c> FNs only that pin; dropping
/// <c>TagWithCallSite</c>, <c>AsTracking</c>, or
/// <c>AsNoTrackingWithIdentityResolution</c> FNs only the matching leftover
/// EF step. Dropping <c>Skip</c>, <c>OrderByDescending</c>, <c>Distinct</c>,
/// <c>ThenBy</c>, or <c>ThenByDescending</c> from
/// <c>ShapePreservingQueryableOperators</c> FNs only that query-root pin.
/// <c>Take</c> and <c>OrderBy</c> stay on the shipped composed-chain fixture.
/// Dropping <c>Last</c>, <c>LastOrDefault</c>, <c>Single</c>, or
/// <c>SingleOrDefault</c> from <c>MaterializerNames</c> FNs only the query-root
/// element pin; the shipped collection-extraction <c>orders.Last()</c> fixtures
/// stay green because they use a different proof. Dropping <c>FirstAsync</c>,
/// <c>LastAsync</c>, <c>LastOrDefaultAsync</c>, <c>SingleAsync</c>,
/// <c>SingleOrDefaultAsync</c>, or <c>ToArrayAsync</c> FNs only that async pin.
/// Dropping <c>DistinctBy</c> from <c>TryGetSupportedCollectionCallback</c> FNs
/// only the DistinctBy callback pin. Queries stored in locals, repository
/// roots, <c>Select</c>/<c>Join</c>, temporal APIs, and <c>Find*</c> stay
/// documented false negatives and are not locked as quiet.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    [Theory]
    [InlineData("db.Orders.IgnoreQueryFilters()")]
    [InlineData("db.Orders.TagWithCallSite()")]
    [InlineData("db.Orders.AsTracking()")]
    [InlineData("db.Orders.AsNoTrackingWithIdentityResolution()")]
    public async Task LeftoverEfShapePreservingStep_Reports(string query)
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
    [InlineData("db.Orders.Skip(5)")]
    [InlineData("db.Orders.OrderByDescending(o => o.Id)")]
    [InlineData("db.Orders.Distinct()")]
    [InlineData("db.Orders.OrderBy(o => o.Id).ThenBy(o => o.Status)")]
    [InlineData("db.Orders.OrderBy(o => o.Id).ThenByDescending(o => o.Status)")]
    public async Task LeftoverQueryableShapePreservingStep_Reports(string query)
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
    [InlineData("db.Orders.Last()")]
    [InlineData("db.Orders.LastOrDefault()")]
    [InlineData("db.Orders.Single()")]
    [InlineData("db.Orders.SingleOrDefault()")]
    public async Task LeftoverQueryRootElementMaterializer_Reports(string materializer)
    {
        await VerifyQueryConsumerAsync(
            @"
    void Main()
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
    [InlineData("await db.Orders.FirstAsync()", false)]
    [InlineData("await db.Orders.LastAsync()", false)]
    [InlineData("await db.Orders.LastOrDefaultAsync()", false)]
    [InlineData("await db.Orders.SingleAsync()", false)]
    [InlineData("await db.Orders.SingleOrDefaultAsync()", false)]
    [InlineData("await db.Orders.ToArrayAsync()", true)]
    public async Task LeftoverQueryRootAsyncMaterializer_Reports(
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
    public async Task LeftoverDistinctByCallback_Reports()
    {
        await VerifyOnNet80Async(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        var unique = orders.DistinctBy(o => {|#0:o.Customer|}.Name);
    }
",
            Diagnostic(0, "Customer", "Order")
        );
    }
}
