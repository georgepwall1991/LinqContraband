using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A <c>while</c>, <c>do</c> or <c>for</c> loop that keeps querying until the database says it is done
/// (keyset batches, paged reads, batched deletes, outbox polling, retries) runs one query per batch or
/// attempt, not one per item. Those loops stay quiet; a loop that walks its own item source still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static string LoopProgram(string body) => Usings + @"
using System.Threading;

class Program
{
    async Task Run(MyDbContext db, CancellationToken ct, int[] ids, Queue<int> queue, int count)
    {
        " + body + @"
    }

    static void Handle(List<User> users) { }
}

namespace System.Threading
{
    // The test reference assemblies predate .NET 6's PeriodicTimer.
    public sealed class PeriodicTimer : IDisposable
    {
        public PeriodicTimer(TimeSpan period) { }
        public System.Threading.Tasks.ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default) => default;
        public void Dispose() { }
    }
}
" + MockNamespace;

    [Theory]
    // Keyset batching: the batch decides when the loop ends.
    [InlineData(@"var last = 0;
        while (true)
        {
            var batch = await db.Users.Where(u => u.Id > last).OrderBy(u => u.Id).Take(500).ToListAsync();
            if (batch.Count == 0) break;
            last = batch[batch.Count - 1].Id;
            Handle(batch);
        }")]
    // Same with a do/while whose condition reads the batch.
    [InlineData(@"var last = 0;
        List<User> batch;
        do
        {
            batch = await db.Users.Where(u => u.Id > last).OrderBy(u => u.Id).Take(500).ToListAsync();
            if (batch.Count > 0) last = batch[batch.Count - 1].Id;
        }
        while (batch.Count == 500);")]
    // A flag derived from the batch drives the loop.
    [InlineData(@"var page = 0;
        var hasMore = true;
        while (hasMore)
        {
            var batch = db.Users.OrderBy(u => u.Id).Skip(page * 100).Take(100).ToList();
            hasMore = batch.Count == 100;
            page++;
        }")]
    // Paged for loop with a counter bound, stopping on an empty page.
    [InlineData(@"for (var page = 0; page < count; page++)
        {
            var batch = db.Users.OrderBy(u => u.Id).Skip(page * 100).Take(100).ToList();
            if (!batch.Any()) break;
            Handle(batch);
        }")]
    // Endless for loop, stopping via return.
    [InlineData(@"for (;;)
        {
            var batch = await db.Users.Take(100).ToListAsync();
            if (batch.Count == 0) return;
            Handle(batch);
        }")]
    // Batched delete in the condition.
    [InlineData(@"while (await db.Users.Where(u => u.Id < 0).Take(1000).ExecuteDeleteAsync() > 0)
        {
        }")]
    // Drain loop: keep going while rows remain.
    [InlineData(@"while (await db.Users.AnyAsync())
        {
            await db.Users.Take(1000).ExecuteDeleteAsync();
        }")]
    public Task BatchLoopDrivenByTheQueryResult_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Theory]
    // BackgroundService-style polling.
    [InlineData(@"while (!ct.IsCancellationRequested)
        {
            var pending = await db.Users.Where(u => u.Id > 0).ToListAsync();
            Handle(pending);
            await Task.Delay(1000, ct);
        }")]
    [InlineData(@"while (true)
        {
            var pending = db.Users.ToList();
            Handle(pending);
            Thread.Sleep(500);
        }")]
    [InlineData(@"using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var pending = await db.Users.ToListAsync();
            Handle(pending);
        }")]
    public Task PollingLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Theory]
    [InlineData(@"for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var users = await db.Users.ToListAsync();
                Handle(users);
                break;
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    [InlineData(@"while (true)
        {
            try
            {
                var users = db.Users.ToList();
                Handle(users);
                return;
            }
            catch (TimeoutException)
            {
                await Task.Yield();
            }
        }")]
    public Task CatchGuardedRetryLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Theory]
    // An item source in the condition makes it a per-item lookup, even with a result-driven break.
    [InlineData(@"while (queue.TryDequeue(out var id))
        {
            var user = {|LC007:db.Users.Find(id)|};
            if (user == null) break;
        }")]
    [InlineData(@"for (var i = 0; i < ids.Length; i++)
        {
            var user = {|LC007:db.Users.Find(ids[i])|};
            if (user == null) break;
        }")]
    // Polling does not excuse a loop over its own items.
    [InlineData(@"while (queue.TryDequeue(out var id))
        {
            var user = {|LC007:db.Users.Find(id)|};
            await Task.Delay(10);
        }")]
    // The result is never used to leave the loop.
    [InlineData(@"var i = 0;
        while (i < count)
        {
            var users = {|LC007:db.Users.Where(u => u.Id == i).ToList()|};
            Handle(users);
            i++;
        }")]
    // A nested lookup inside a batch loop is still per item.
    [InlineData(@"while (true)
        {
            var batch = await db.Users.Take(100).ToListAsync();
            if (batch.Count == 0) break;
            foreach (var user in batch)
            {
                var again = {|LC007:db.Users.Find(user.Id)|};
            }
        }")]
    // A batch loop per outer item is still one query per outer item.
    [InlineData(@"foreach (var id in ids)
        {
            while (true)
            {
                var batch = {|LC007:db.Users.Where(u => u.Id == id).Take(100).ToList()|};
                if (batch.Count == 0) break;
            }
        }")]
    // A catch without a success exit is not a retry.
    [InlineData(@"for (var i = 0; i < count; i++)
        {
            try
            {
                var users = {|LC007:db.Users.Where(u => u.Id == i).ToList()|};
                Handle(users);
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    public Task PerItemLoop_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));
}
