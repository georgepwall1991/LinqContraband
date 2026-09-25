using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    [Fact(Timeout = 60000)]
    public async Task ExecuteDelete_OnInstanceHelperResult_TerminatesAndReadsHelperFilter()
    {
        // VirtoCommerce's UserSignInLogService: the build hung here, because the walk moved from the
        // helper call's implicit `this` receiver back up to the call itself, forever.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Log { public int Id { get; set; } public System.DateTime CreatedDate { get; set; } }

    public class LogService
    {
        private readonly DbContext _db;
        public LogService(DbContext db) => _db = db;

        public virtual Task<int> DeleteOlderThan(System.DateTime cutoff, int batchSize)
            => BuildExpiredQuery(cutoff, batchSize).ExecuteDeleteAsync();

        public int DeleteEverything() => {|LC035:BuildAllQuery().ExecuteDelete()|};

        protected virtual IQueryable<Log> BuildExpiredQuery(System.DateTime cutoff, int batchSize)
        {
            return _db.Set<Log>().Where(x => x.CreatedDate < cutoff).OrderBy(x => x.CreatedDate).Take(batchSize);
        }

        protected IQueryable<Log> BuildAllQuery() => _db.Set<Log>().OrderBy(x => x.Id);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperParameterFilteredByEveryCaller_ShouldNotTrigger()
    {
        // Blogifier and Smartstore filter in the public method and delete in a protected helper.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public class Entity { public int Id { get; set; } }

    public class Repository<T> where T : Entity
    {
        private readonly DbContext _db;
        public Repository(DbContext db) => _db = db;

        public Task DeleteAsync(System.Collections.Generic.IEnumerable<int> ids)
        {
            var query = _db.Set<T>().Where(m => ids.Contains(m.Id));
            return DeleteInternalAsync(query);
        }

        public Task DeleteOneAsync(int id) => DeleteInternalAsync(_db.Set<T>().Where(m => m.Id == id));

        protected static async Task DeleteInternalAsync(IQueryable<T> query) => await query.ExecuteDeleteAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperCalledWithBareDbSet_ShouldTriggerOnArgument()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int DeleteOne(DbContext db, int id) => DeleteAll(db.Set<User>().Where(u => u.Id == id));

        public int Purge(DbContext db) => DeleteAll({|LC035:db.Set<User>()|});

        private static int DeleteAll(IQueryable<User> query) => query.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ExtensionHelperCalledOnFilteredAndBareQueries_ShouldTriggerOnBareReceiver()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    internal static class BulkExtensions
    {
        public static int DeleteAll<T>(this IQueryable<T> source) => source.ExecuteDelete();
    }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            db.Set<User>().Where(u => u.Id > 10).DeleteAll();
            {|LC035:db.Set<User>()|}.DeleteAll();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_PublicHelperWithoutCallers_ShouldTriggerOnExecute()
    {
        // No call site in the compilation: the caller is unknown, so the report stays on the execute.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int DeleteAll(IQueryable<User> query) => {|LC035:query.ExecuteDelete()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperParameterPassedThroughFilteredCallerChain_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int id) => Middle(db.Set<User>().Where(u => u.Id == id));

        private static int Middle(IQueryable<User> query) => Inner(query.TagWith(""bulk""));

        private static int Inner(IQueryable<User> query) => query.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperParameterPassedThroughUnfilteredCallerChain_ShouldTriggerOnOuterArgument()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db) => Middle({|LC035:db.Set<User>()|});

        private static int Middle(IQueryable<User> query) => Inner(query.TagWith(""bulk""));

        private static int Inner(IQueryable<User> query) => query.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperUsedAsMethodGroup_ShouldTriggerOnExecute()
    {
        // A delegate can be invoked with any query, so its callers cannot be seen.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int id)
        {
            Func<IQueryable<User>, int> delete = DeleteAll;
            return DeleteAll(db.Set<User>().Where(u => u.Id == id));
        }

        private static int DeleteAll(IQueryable<User> query) => {|LC035:query.ExecuteDelete()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_VirtualHelper_ShouldTriggerOnExecute()
    {
        // Calls through the base or an override target another symbol, so the callers are not all visible.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public class Program
    {
        public int Run(DbContext db, int id) => DeleteAll(db.Set<User>().Where(u => u.Id == id));

        protected virtual int DeleteAll(IQueryable<User> query) => {|LC035:query.ExecuteDelete()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_HelperReassignsItsParameter_ShouldTriggerOnExecute()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int id) => DeleteAll(db, db.Set<User>().Where(u => u.Id == id));

        private static int DeleteAll(DbContext db, IQueryable<User> query)
        {
            query = db.Set<User>();
            return {|LC035:query.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_LocalFunctionParameterFilteredByCaller_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int id)
        {
            return Delete(db.Set<User>().Where(u => u.Id == id));

            static int Delete(IQueryable<User> query) => query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_RecursiveHelperFilteredByOuterCaller_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int id) => DeleteBatches(db.Set<User>().Where(u => u.Id > id), 3);

        private static int DeleteBatches(IQueryable<User> query, int remaining)
        {
            var deleted = query.Take(100).ExecuteDelete();
            return remaining == 0 ? deleted : deleted + DeleteBatches(query, remaining - 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ProjectFilterHelper_ShouldNotTrigger()
    {
        // The helper's body applies Where to its parameter and returns it.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Grant { public DateTime Expiration { get; set; } }

    internal static class GrantFilters
    {
        public static IQueryable<Grant> Expired(this IQueryable<Grant> source, DateTime now) =>
            source.Where(g => g.Expiration < now);
    }

    public sealed class Cleanup
    {
        public async Task Run(DbContext db)
        {
            await ApplyExpiredFilter(db.Set<Grant>()).ExecuteDeleteAsync();
            await db.Set<Grant>().Expired(DateTime.UtcNow).TagWith(""cleanup"").ExecuteDeleteAsync();
        }

        private static IQueryable<Grant> ApplyExpiredFilter(IQueryable<Grant> query)
        {
            var now = DateTime.UtcNow;
            return query.Where(g => g.Expiration < now);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ProjectHelperThatDoesNotFilterOnEveryPath_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Grant { public DateTime Expiration { get; set; } }

    public sealed class Cleanup
    {
        public void Run(DbContext db, bool onlyExpired)
        {
            {|LC035:Tagged(db.Set<Grant>()).ExecuteDelete()|};
            {|LC035:MaybeExpired(db.Set<Grant>(), onlyExpired).ExecuteDelete()|};
        }

        private static IQueryable<Grant> Tagged(IQueryable<Grant> query) => query.TagWith(""cleanup"");

        private static IQueryable<Grant> MaybeExpired(IQueryable<Grant> query, bool onlyExpired)
        {
            if (onlyExpired)
                return query.Where(g => g.Expiration < DateTime.UtcNow);
            return query;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
