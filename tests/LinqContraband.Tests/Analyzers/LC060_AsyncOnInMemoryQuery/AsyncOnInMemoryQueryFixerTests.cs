using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery.AsyncOnInMemoryQueryAnalyzer,
    LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery.AsyncOnInMemoryQueryFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC060_AsyncOnInMemoryQuery;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// The wrapper method keeps another await, so dropping one does not leave an async method without awaits.
/// </summary>
public class AsyncOnInMemoryQueryFixerTests
{
    private static string Wrap(string body) => AsyncOnInMemoryQueryTests.Wrap("await Task.Yield();\n" + body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Theory]
    [InlineData(
        @"var items = await list.AsQueryable().Where(x => x.Active).{|LC060:ToListAsync|}(ct);",
        @"var items = list.AsQueryable().Where(x => x.Active).ToList();")]
    [InlineData(
        @"var items = await list.AsQueryable().{|LC060:ToListAsync|}();",
        @"var items = list.AsQueryable().ToList();")]
    [InlineData(
        @"var items = await list.AsQueryable().{|LC060:ToArrayAsync|}(ct);",
        @"var items = list.AsQueryable().ToArray();")]
    [InlineData(
        @"var items = await list.AsQueryable().{|LC060:ToHashSetAsync|}(ct);",
        @"var items = list.AsQueryable().ToHashSet();")]
    [InlineData(
        @"var count = await array.AsQueryable().{|LC060:CountAsync|}(ct);",
        @"var count = array.AsQueryable().Count();")]
    [InlineData(
        @"var count = await array.AsQueryable().{|LC060:CountAsync|}(x => x.Active, ct);",
        @"var count = array.AsQueryable().Count(x => x.Active);")]
    [InlineData(
        @"var count = await array.AsQueryable().{|LC060:LongCountAsync|}(ct);",
        @"var count = array.AsQueryable().LongCount();")]
    [InlineData(
        @"var item = await list.AsQueryable().{|LC060:FirstOrDefaultAsync|}(x => x.Active, ct);",
        @"var item = list.AsQueryable().FirstOrDefault(x => x.Active);")]
    [InlineData(
        @"var item = await list.AsQueryable().{|LC060:FirstAsync|}(ct);",
        @"var item = list.AsQueryable().First();")]
    [InlineData(
        @"var item = await list.AsQueryable().{|LC060:SingleOrDefaultAsync|}(x => x.Id == 1, ct);",
        @"var item = list.AsQueryable().SingleOrDefault(x => x.Id == 1);")]
    [InlineData(
        @"var item = await list.AsQueryable().OrderBy(x => x.Id).{|LC060:LastOrDefaultAsync|}(ct);",
        @"var item = list.AsQueryable().OrderBy(x => x.Id).LastOrDefault();")]
    [InlineData(
        @"var any = await list.AsQueryable().{|LC060:AnyAsync|}(x => x.Active, ct);",
        @"var any = list.AsQueryable().Any(x => x.Active);")]
    [InlineData(
        @"var all = await list.AsQueryable().{|LC060:AllAsync|}(x => x.Active, ct);",
        @"var all = list.AsQueryable().All(x => x.Active);")]
    [InlineData(
        @"var has = await list.AsQueryable().{|LC060:ContainsAsync|}(list[0], ct);",
        @"var has = list.AsQueryable().Contains(list[0]);")]
    [InlineData(
        @"var total = await list.AsQueryable().{|LC060:SumAsync|}(x => x.Price, ct);",
        @"var total = list.AsQueryable().Sum(x => x.Price);")]
    [InlineData(
        @"var max = await list.AsQueryable().{|LC060:MaxAsync|}(x => x.Id, ct);",
        @"var max = list.AsQueryable().Max(x => x.Id);")]
    [InlineData(
        @"var byId = await list.AsQueryable().{|LC060:ToDictionaryAsync|}(x => x.Id, ct);",
        @"var byId = list.AsQueryable().ToDictionary(x => x.Id);")]
    [InlineData(
        @"var byId = await list.AsQueryable().{|LC060:ToDictionaryAsync|}(x => x.Id, EqualityComparer<int>.Default, ct);",
        @"var byId = list.AsQueryable().ToDictionary(x => x.Id, EqualityComparer<int>.Default);")]
    // ConfigureAwait goes with the await.
    [InlineData(
        @"var items = await list.AsQueryable().{|LC060:ToListAsync|}(ct).ConfigureAwait(false);",
        @"var items = list.AsQueryable().ToList();")]
    // Through a local and inside a larger expression.
    [InlineData(
        @"var q = list.AsQueryable().Where(x => x.Active); var count = (await q.{|LC060:ToListAsync|}(ct)).Count;",
        @"var q = list.AsQueryable().Where(x => x.Active); var count = (q.ToList()).Count;")]
    [InlineData(
        @"return await list.AsQueryable().{|LC060:AnyAsync|}(ct);",
        @"return list.AsQueryable().Any();")]
    // Multi-line chains keep their layout.
    [InlineData(
        @"var items = await list
            .AsQueryable()
            .Where(x => x.Active)
            .{|LC060:ToListAsync|}(ct);",
        @"var items = list
            .AsQueryable()
            .Where(x => x.Active)
            .ToList();")]
    public async Task ReplacesTheAwaitedCallWithItsSynchronousForm(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }

    [Theory]
    // Fixer coverage for every other reporting shape in AsyncOnInMemoryQueryTests.
    [InlineData(
        @"var items = await list.AsQueryable().AsNoTracking().Include(x => x.Parent).TagWith(""t"").{|LC060:ToListAsync|}(ct);",
        @"var items = list.AsQueryable().AsNoTracking().Include(x => x.Parent).TagWith(""t"").ToList();")]
    [InlineData(
        @"var items = await Queryable.AsQueryable(list).{|LC060:ToListAsync|}(ct);",
        @"var items = Queryable.AsQueryable(list).ToList();")]
    [InlineData(
        @"var items = await new EnumerableQuery<Item>(list).{|LC060:ToListAsync|}(ct);",
        @"var items = new EnumerableQuery<Item>(list).ToList();")]
    [InlineData(
        @"var items = await ((IEnumerable<Item>)list).AsQueryable().{|LC060:ToListAsync|}(ct);",
        @"var items = ((IEnumerable<Item>)list).AsQueryable().ToList();")]
    [InlineData(
        @"var items = await sequence.Select(x => x).AsQueryable().{|LC060:ToListAsync|}(ct);",
        @"var items = sequence.Select(x => x).AsQueryable().ToList();")]
    [InlineData(
        @"IQueryable<Item> q = ct.CanBeCanceled ? list.AsQueryable() : array.AsQueryable(); var count = await q.{|LC060:CountAsync|}(ct);",
        @"IQueryable<Item> q = ct.CanBeCanceled ? list.AsQueryable() : array.AsQueryable(); var count = q.Count();")]
    [InlineData(
        @"var items = await TestData.Items().{|LC060:ToListAsync|}(ct);",
        @"var items = TestData.Items().ToList();")]
    [InlineData(
        @"var items = await list.AsQueryable().ActiveOnly().{|LC060:ToListAsync|}(ct);",
        @"var items = list.AsQueryable().ActiveOnly().ToList();")]
    public async Task EveryOtherReportingShape_GetsACompilingFix(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }

    [Theory]
    // The only await in its lambda or local function (CS1998).
    [InlineData(@"Func<Task<List<Item>>> load = async () => await list.AsQueryable().{|LC060:ToListAsync|}(ct);")]
    [InlineData(@"async Task<int> CountAsync() => await array.AsQueryable().{|LC060:CountAsync|}(ct);")]
    // No synchronous twin with the same shape.
    [InlineData(@"await list.AsQueryable().{|LC060:ForEachAsync|}(x => x.Active = false, ct);")]
    [InlineData(@"await list.AsQueryable().{|LC060:LoadAsync|}(ct);")]
    [InlineData(@"await foreach (var x in list.AsQueryable().{|LC060:AsAsyncEnumerable|}()) { }")]
    // Not awaited where it is called.
    [InlineData(@"var task = list.AsQueryable().{|LC060:ToListAsync|}(ct); var items = await task;")]
    [InlineData(@"var tasks = new[] { list.AsQueryable().{|LC060:CountAsync|}(ct) }; await Task.WhenAll(tasks);")]
    // Static call form and named arguments.
    [InlineData(@"var items = await EntityFrameworkQueryableExtensions.{|LC060:ToListAsync|}(list.AsQueryable(), ct);")]
    [InlineData(@"var items = await list.AsQueryable().{|LC060:ToListAsync|}(cancellationToken: ct);")]
    public async Task NoSafeRewrite_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code);
    }

    [Fact]
    public async Task OnlyAwaitInAsyncMethod_OffersNoFix()
    {
        // Removing the only await would leave an async method without one (CS1998).
        var code = AsyncOnInMemoryQueryTests.Wrap("").Replace(
            "class Program\n{",
            "class Program\n{\n    async Task<List<Item>> Load(List<Item> items, CancellationToken ct) => await items.AsQueryable().{|LC060:ToListAsync|}(ct);\n");
        Assert.Contains("Load(", code);
        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task FixAll_RewritesEveryCall()
    {
        var fixedCode = Wrap(@"var items = list.AsQueryable().ToList();
        var count = array.AsQueryable().Count();");
        await new CodeFixTest
        {
            TestCode = Wrap(@"var items = await list.AsQueryable().{|LC060:ToListAsync|}(ct);
        var count = await array.AsQueryable().{|LC060:CountAsync|}(ct);"),
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode
        }.RunAsync();
    }
}
