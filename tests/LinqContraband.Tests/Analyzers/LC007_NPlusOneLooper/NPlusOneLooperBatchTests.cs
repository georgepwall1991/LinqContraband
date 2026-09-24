namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Batches that Kavita uses to keep a query's <c>IN</c> list or result size bounded: <c>foreach</c> over
/// <c>ids.Chunk(n)</c> with the query reading the whole chunk, and a counted loop that pages with
/// <c>Skip(page * size).Take(size)</c>. Both run one query per batch, not one per item, so they stay quiet. A query
/// that reads one element of the chunk, a query per item inside the chunk, and a page of one row still report.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Theory]
    // The chunk is read with Contains inside the query.
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            var users = await db.Users.Where(u => batch.Contains(u.Id)).ToListAsync();
        }")]
    // The chunk is copied into a set first, as Kavita's statistics service does.
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            var batchSet = batch.ToHashSet();
            var users = db.Users.Where(u => batchSet.Contains(u.ParentId)).Select(u => u.Id).ToList();
        }")]
    // Enumerable.Contains with the chunk as an argument, results appended to an outer list.
    [InlineData(@"var removed = new List<User>();
        foreach (var batch in ids.Chunk(50))
        {
            removed.AddRange(await db.Users.Where(u => Enumerable.Contains(batch, u.Id)).ToListAsync());
        }")]
    // Paged by a for counter.
    [InlineData(@"var total = await db.Users.CountAsync();
        var pages = (total + 99) / 100;
        for (var page = 0; page < pages; page++)
        {
            var users = await db.Users.OrderBy(u => u.Id).Skip(page * 100).Take(100).ToListAsync();
        }")]
    // Paged by a while counter the body advances.
    [InlineData(@"var total = await db.Users.CountAsync();
        var offset = 0;
        while (offset < total)
        {
            var users = db.Users.OrderBy(u => u.Id).Skip(offset).Take(size).ToList();
            offset += size;
        }")]
    public Task ChunkedOrPagedBatchLoop_IsQuiet(string body) =>
        VerifyBatchAsync(body);

    [Theory]
    // Chunked, but the query runs once per item of the chunk.
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            foreach (var id in batch)
            {
                var user = {|LC007:db.Users.Where(u => u.Id == id).ToList()|};
            }
        }")]
    // Chunked, but the query reads a single element of it.
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            var user = {|LC007:db.Users.Where(u => u.Id == batch.First()).ToList()|};
        }")]
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            var user = {|LC007:db.Users.Where(u => u.Id == batch[0]).ToList()|};
        }")]
    // Chunked, but the query does not read the chunk at all.
    [InlineData(@"foreach (var batch in ids.Chunk(500))
        {
            var users = {|LC007:db.Users.ToList()|};
        }")]
    // Not a chunk: a plain foreach over ids with Contains on a one-element array.
    [InlineData(@"foreach (var id in ids)
        {
            var single = new[] { id };
            var users = {|LC007:db.Users.Where(u => single.Contains(u.Id)).ToList()|};
        }")]
    // A page of one row per iteration is one query per row.
    [InlineData(@"for (var i = 0; i < size; i++)
        {
            var users = {|LC007:db.Users.OrderBy(u => u.Id).Skip(i).Take(1).ToList()|};
        }")]
    // Skip does not follow the counter, so every iteration reads the same page.
    [InlineData(@"for (var i = 0; i < size; i++)
        {
            var users = {|LC007:db.Users.OrderBy(u => u.Id).Skip(size).Take(100).ToList()|};
        }")]
    // The counter walks an item source.
    [InlineData(@"for (var i = 0; i < ids.Length; i++)
        {
            var users = {|LC007:db.Users.Where(u => u.Id == ids[i]).Skip(i).Take(100).ToList()|};
        }")]
    public Task PerItemQueryInBatchLoop_StillReports(string body) =>
        VerifyBatchAsync(body);

    // Enumerable.Chunk needs .NET 6 or later reference assemblies.
    private static Task VerifyBatchAsync(string body) =>
        new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = BatchProgram(body),
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80,
        }.RunAsync();

    private static string BatchProgram(string body) => Usings + @"
class Program
{
    async Task Run(MyDbContext db, int[] ids, int size)
    {
        " + body + @"
    }
}
" + MockNamespace;
}
