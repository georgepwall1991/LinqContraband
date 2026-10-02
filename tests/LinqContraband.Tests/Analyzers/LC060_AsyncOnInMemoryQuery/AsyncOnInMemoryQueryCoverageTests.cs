using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery.AsyncOnInMemoryQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC060_AsyncOnInMemoryQuery;

/// <summary>
/// Leftover 5.16.0 LC060 <c>AsyncIncapable</c> arms the original 57 analyzer cases do not isolate.
/// A concrete <c>HashSet&lt;T&gt;</c> is the class-or-struct leaf besides <c>List&lt;T&gt;</c>.
/// <c>SelectMany</c>, <c>Concat</c>, <c>ThenBy</c> and <c>Distinct</c> are named LINQ to Objects
/// iterators that <c>IsLinqToObjectsIterator</c> accepts; the shipped fixtures only cover
/// <c>Where</c>, <c>Select</c>, <c>OrderBy</c>, <c>Skip</c> and <c>Take</c>.
/// An interface-typed local is followed through its writes: it reports when every write is a concrete
/// collection, an array or a LINQ to Objects iterator, and stays quiet when any write is unproven.
/// </summary>
public class AsyncOnInMemoryQueryCoverageTests
{
    [Fact]
    public async Task HashSet_AsQueryable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(@"
            var set = new HashSet<Item>();
            var items = await set.AsQueryable().{|LC060:ToListAsync|}(ct);"));
    }

    [Fact]
    public async Task SelectMany_ThenAsQueryable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(@"
            var items = await list.SelectMany(_ => array).AsQueryable().{|LC060:ToListAsync|}(ct);"));
    }

    [Fact]
    public async Task Concat_ThenAsQueryable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(@"
            var items = await list.Concat(array).AsQueryable().{|LC060:ToListAsync|}(ct);"));
    }

    [Fact]
    public async Task ThenBy_ThenAsQueryable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(@"
            var items = await list.OrderBy(x => x.Id).ThenBy(x => x.Price).AsQueryable().{|LC060:ToListAsync|}(ct);"));
    }

    [Fact]
    public async Task Distinct_ThenAsQueryable_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(@"
            var items = await list.Distinct().AsQueryable().{|LC060:ToListAsync|}(ct);"));
    }

    [Theory]
    [InlineData(@"IList<Item> items = list; var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"ICollection<Item> items = new List<Item>(); var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"ISet<Item> items = new HashSet<Item>(); var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"IEnumerable<Item> items = list; var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"IEnumerable<Item> items = list.Where(x => x.Active); var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"IEnumerable<Item> items = list; if (ct.CanBeCanceled) items = array; var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"IEnumerable<Item> first = list; IEnumerable<Item> items = first; var result = await items.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    public async Task InterfaceLocal_AssignedOnlyFromInMemorySources_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(body));
    }

    [Theory]
    // A write the walk cannot see.
    [InlineData(@"IEnumerable<Item> items; items = list; Replace(ref items); var result = await items.AsQueryable().ToListAsync(ct); void Replace(ref IEnumerable<Item> target) { }")]
    // A local with no write the walk can follow.
    [InlineData(@"foreach (IEnumerable<Item> items in new[] { list }) { var result = await items.AsQueryable().ToListAsync(ct); }")]
    // Cast and OfType can hand back their source.
    [InlineData(@"IEnumerable<Item> items = sequence.OfType<Item>(); var result = await items.AsQueryable().ToListAsync(ct);")]
    public async Task InterfaceLocal_WithAnUnprovenWrite_StaysQuiet(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(AsyncOnInMemoryQueryTests.Wrap(body));
    }
}
