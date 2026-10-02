using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery.AsyncOnInMemoryQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC060_AsyncOnInMemoryQuery;

/// <summary>
/// Leftover 5.16.0 LC060 <c>AsyncIncapable</c> arms the original 57 analyzer cases do not isolate.
/// A concrete <c>HashSet&lt;T&gt;</c> is the class-or-struct leaf besides <c>List&lt;T&gt;</c>.
/// <c>SelectMany</c>, <c>Concat</c>, <c>ThenBy</c> and <c>Distinct</c> are named LINQ to Objects
/// iterators that <c>IsLinqToObjectsIterator</c> accepts; the shipped fixtures only cover
/// <c>Where</c>, <c>Select</c>, <c>OrderBy</c>, <c>Skip</c> and <c>Take</c>.
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
}
