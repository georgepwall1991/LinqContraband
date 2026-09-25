using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Batch drain loops, as in Duende IdentityServer's <c>TokenCleanupService</c> and Smartstore's cleanup tasks: each
/// iteration reads or deletes the next <c>Take(n)</c> batch and the loop ends once a batch comes back short. The loop
/// runs about rows / n times, not once per item, so every query in it stays quiet. A loop whose exit does not depend
/// on the batch, or that walks an item source of its own, still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static string DrainProgram(string body) => Usings + @"
using System.Threading;

class CleanupOptions
{
    public int BatchSize { get; set; } = 100;
}

class Program
{
    private readonly CleanupOptions _options = new CleanupOptions();
    private readonly MyDbContext _db = new MyDbContext();
    private int BatchSize => _options.BatchSize;

    async Task Run(MyDbContext db, CancellationToken ct, int[] ids, Queue<int> queue, int count, int batchSize)
    {
        " + body + @"
    }

    static void Handle(IEnumerable<User> users) { }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class DrainQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source, System.Threading.CancellationToken ct) => Task.FromResult(new T[0]);
        public static Task<int> ExecuteDeleteAsync<T>(this IQueryable<T> source, System.Threading.CancellationToken ct) => Task.FromResult(0);
    }
}
" + MockNamespace;

    [Theory]
    // Duende: the count ExecuteDeleteAsync returns for a Take-bounded batch ends the loop.
    [InlineData(@"var found = int.MaxValue;
        var query = db.Users.Where(x => x.Id < count).OrderBy(x => x.Id);
        while (found >= batchSize)
        {
            found = await query.Take(batchSize).ExecuteDeleteAsync(ct);
        }")]
    // Same, with the batch size read from options.
    [InlineData(@"var found = int.MaxValue;
        var query = db.Users.Where(x => x.Id < count).OrderBy(x => x.Id);
        while (found >= _options.BatchSize)
        {
            found = await query.Take(_options.BatchSize).ExecuteDeleteAsync(ct);
        }")]
    // Duende: read a batch, delete its key range, stop when the batch is short.
    [InlineData(@"var found = int.MaxValue;
        while (found >= batchSize)
        {
            var q = db.Users.Where(x => x.Id < count).OrderBy(x => x.Id);
            var expired = await q.Take(batchSize).AsNoTracking().ToArrayAsync(ct);
            found = expired.Length;
            if (found > 0)
            {
                await q.Where(pg => pg.Id >= expired.First().Id && pg.Id <= expired.Last().Id).ExecuteDeleteAsync(ct);
            }
        }")]
    // Duende with the options-backed batch size, as the real service reads it.
    [InlineData(@"var found = int.MaxValue;
        while (found >= _options.BatchSize)
        {
            var q = _db.Users.Where(x => x.Id < count).OrderBy(x => x.Id);
            var expired = await q.Take(_options.BatchSize).AsNoTracking().ToArrayAsync(ct);
            found = expired.Length;
            if (found > 0)
            {
                await q.Where(pg => pg.Id >= expired.First().Id && pg.Id <= expired.Last().Id).ExecuteDeleteAsync(ct);
            }
        }")]
    // Batch size from a property of this class.
    [InlineData(@"var found = int.MaxValue;
        while (found >= BatchSize)
        {
            found = await db.Users.Where(x => x.Id < count).Take(BatchSize).ExecuteDeleteAsync(ct);
        }")]
    // Smartstore: take ids, stop when empty, delete them.
    [InlineData(@"var q = db.Users.Where(x => x.ParentId == 0);
        while (true)
        {
            var batchIds = q.Take(100).Select(x => x.Id).ToArray();
            if (batchIds.Length == 0) break;
            db.Users.Where(x => batchIds.Contains(x.Id)).ExecuteDelete();
        }")]
    // do/while form, the batch's count decides.
    [InlineData(@"List<User> batch;
        do
        {
            batch = await db.Users.Where(x => x.ParentId == 0).Take(500).ToListAsync();
            await db.Users.Where(x => x.ParentId == 0 && x.Id <= batch.Count).ExecuteDeleteAsync();
        }
        while (batch.Count == 500);")]
    // The emptiness of the batch, via Any(), ends the loop.
    [InlineData(@"while (true)
        {
            var batch = db.Users.Where(x => x.ParentId == 0).Take(200).ToList();
            if (!batch.Any()) break;
            db.Users.Where(x => x.ParentId == batch[0].Id).ExecuteUpdate(x => new User { ParentId = 1 });
        }")]
    public Task BatchDrainLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(DrainProgram(body));

    [Theory]
    // A query per queued item: the batch query is bounded, but the queue drives the loop.
    [InlineData(@"while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var batch = {|LC007:db.Users.Where(x => x.ParentId == id).Take(100).ToList()|};
            if (batch.Count == 0) break;
            {|LC007:db.Users.Where(x => x.Id == id).ExecuteDelete()|};
        }")]
    // Take-bounded, but the exit never looks at the batch.
    [InlineData(@"var i = 0;
        while (i < count)
        {
            var batch = {|LC007:db.Users.Where(x => x.ParentId == i).Take(100).ToList()|};
            Handle(batch);
            {|LC007:db.Users.Where(x => x.ParentId == i).ExecuteDelete()|};
            i++;
        }")]
    // The exit depends on a query that is not batch-bounded, so the sibling delete is per iteration.
    [InlineData(@"var i = 0;
        while (true)
        {
            var user = db.Users.FirstOrDefault(x => x.Id == i);
            if (user == null) break;
            {|LC007:db.Users.Where(x => x.ParentId == user.Id).ExecuteDelete()|};
            i++;
        }")]
    // A per-item lookup nested in a drain loop is still per item.
    [InlineData(@"var found = int.MaxValue;
        while (found >= batchSize)
        {
            var batch = await db.Users.Take(batchSize).ToListAsync();
            found = batch.Count;
            foreach (var user in batch)
            {
                {|LC007:db.Users.Where(x => x.ParentId == user.Id).ExecuteDelete()|};
            }
        }")]
    // A one-row "batch" is one query per row.
    [InlineData(@"while (true)
        {
            var one = db.Users.Take(1).ToList();
            if (one.Count == 0) break;
            {|LC007:db.Users.Where(x => x.Id == one[0].Id).ExecuteDelete()|};
        }")]
    // foreach over items with a query per item.
    [InlineData(@"foreach (var id in ids)
        {
            var batch = {|LC007:db.Users.Where(x => x.ParentId == id).Take(100).ToList()|};
            if (batch.Count == 0) break;
        }")]
    public Task NotABatchDrainLoop_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(DrainProgram(body));
}
