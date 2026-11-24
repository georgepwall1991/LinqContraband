using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// Leftover LC045 <c>CollectionMutatorMethods</c> names the shipped
/// <c>Items.Add</c> fixtures do not isolate. Dropping leftover
/// <c>AddRange</c> FNs only <see cref="LeftoverAddRange_IsQuiet"/>; the
/// shipped <c>o.Items.Add</c> and <c>order?.Items?.Add</c> fixtures stay
/// green. The same holds for leftover <c>Remove</c>, <c>RemoveRange</c>
/// and <c>Clear</c>. <c>o.Customer.Clear()</c> still reports through the
/// collection-navigation gate. A leftover name on a reference navigation
/// still reports, so a later edit that treats every matching name as a
/// collection mutation is visible. Does not restage leftover preload wrap
/// names owned by draft #738.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    private static Task VerifyLeftoverMutatorQuietAsync(string mutation) =>
        VerifyQueryConsumerAsync(
            @"
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            "
                + mutation
                + @"
        }
        db.SaveChanges();
    }
"
        );

    private static string LeftoverMutatorLookalikeWrap(string body)
    {
        return Usings
            + body
            + MockNamespace.Replace(
                "        public Customer GetDetached() => new Customer { Address = new Address() };",
                @"        public Customer GetDetached() => new Customer { Address = new Address() };
        public void AddRange() { }"
            );
    }

    [Fact]
    public Task LeftoverAddRange_IsQuiet() =>
        VerifyLeftoverMutatorQuietAsync("o.Items.AddRange(new[] { new OrderItem() });");

    [Fact]
    public Task LeftoverRemove_IsQuiet() =>
        VerifyLeftoverMutatorQuietAsync("o.Items.Remove(new OrderItem());");

    [Fact]
    public Task LeftoverRemoveRange_IsQuiet() =>
        VerifyLeftoverMutatorQuietAsync("o.Items.RemoveRange(0, 1);");

    [Fact]
    public Task LeftoverClear_IsQuiet() => VerifyLeftoverMutatorQuietAsync("o.Items.Clear();");

    [Fact]
    public async Task LookalikeAddRange_OnReferenceNav_StillReports()
    {
        // AddRange here is an instance method on Customer, not a collection
        // mutation: evaluating o.Customer is still a read of unloaded data.
        var test = LeftoverMutatorLookalikeWrap(
            @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var orders = db.Orders.ToList();
        foreach (var o in orders)
        {
            {|#0:o.Customer|}.AddRange();
        }
    }
}
"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }
}
