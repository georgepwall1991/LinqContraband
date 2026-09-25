using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    [Fact]
    public async Task ExecuteDelete_FilteredLocalExecutedInBatchLoop_ShouldNotTrigger()
    {
        // Duende IdentityServer's token cleanup builds the filtered query once and deletes it in batches.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db, int batchSize)
        {
            var now = DateTime.UtcNow;
            var found = int.MaxValue;
            var query = db.Set<PersistedGrant>().Where(x => x.Expiration < now).OrderBy(x => x.Expiration);
            while (found >= batchSize)
            {
                found = await query.Take(batchSize).ExecuteDeleteAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_FilteredLocalExecutedInBatchLoopInsideTry_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db, int batchSize)
        {
            try
            {
                var found = int.MaxValue;
                var query = db.Set<PersistedGrant>().Where(x => x.Expiration < DateTime.UtcNow).OrderBy(x => x.Expiration);
                while (found >= batchSize)
                {
                    found = await query.Take(batchSize).ExecuteDeleteAsync();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_FilteredLocalDeclaredInsideLoop_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db, int batchSize)
        {
            var found = int.MaxValue;
            while (found >= batchSize)
            {
                var query = db.Set<PersistedGrant>().Where(x => x.Expiration < DateTime.UtcNow).OrderBy(x => x.Expiration);
                found = await query.Take(batchSize).ExecuteDeleteAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_UnfilteredLocalDeclaredInsideTry_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db, int batchSize)
        {
            try
            {
                var query = db.Set<PersistedGrant>().OrderBy(x => x.Expiration);
                await {|LC035:query.Take(batchSize).ExecuteDeleteAsync()|};
            }
            catch (Exception)
            {
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_FilteredLocalReassignedToUnfilteredInsideLoop_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db, int batchSize)
        {
            var found = int.MaxValue;
            IQueryable<PersistedGrant> query = db.Set<PersistedGrant>().Where(x => x.Expiration < DateTime.UtcNow);
            while (found >= batchSize)
            {
                found = await {|LC035:query.Take(batchSize).ExecuteDeleteAsync()|};
                query = db.Set<PersistedGrant>();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_LoopReassignsFilteredBeforeReadEachPass_ShouldNotTrigger()
    {
        // The unfiltered assignment at the end of the body is overwritten at the top of the next pass.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public void Run(DbContext db, int batches)
        {
            IQueryable<PersistedGrant> query = db.Set<PersistedGrant>();
            for (var i = 0; i < batches; i++)
            {
                query = db.Set<PersistedGrant>().Where(x => x.Expiration < DateTime.UtcNow);
                query.ExecuteDelete();
                query = db.Set<PersistedGrant>();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_InnerLoopReassignsUnfilteredAfterRead_ShouldTrigger()
    {
        // The outer loop's filtered assignment does not run between passes of the inner loop.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public void Run(DbContext db, int batches)
        {
            for (var i = 0; i < batches; i++)
            {
                IQueryable<PersistedGrant> query = db.Set<PersistedGrant>().Where(x => x.Expiration < DateTime.UtcNow);
                for (var j = 0; j < batches; j++)
                {
                    {|LC035:query.ExecuteDelete()|};
                    query = db.Set<PersistedGrant>();
                }
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_CatchReadsLocalFilteredOnlyInTry_ShouldTrigger()
    {
        // The catch can run before the try block's assignment did.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class PersistedGrant { public DateTime? Expiration { get; set; } }

    public sealed class Cleanup
    {
        public void Run(DbContext db)
        {
            IQueryable<PersistedGrant> query = db.Set<PersistedGrant>();
            try
            {
                query = query.Where(x => x.Expiration < DateTime.UtcNow);
            }
            catch (Exception)
            {
                {|LC035:query.ExecuteDelete()|};
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
