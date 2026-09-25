using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// The most common hidden N+1 is a loop that calls a helper in the same project which runs the query. LC007 reads
/// statically bound source helpers up to three calls deep and reports the call in the loop, but stays quiet on cache
/// factories, memoized helpers, overridable methods, metadata-only helpers, query builders and drain loops.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string HelperMocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class HelperTestQueryableExtensions
    {
        public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate) => Task.FromResult(default(T));
    }
}

namespace Microsoft.Extensions.Caching.Memory
{
    public interface IMemoryCache { }

    public static class CacheExtensions
    {
        public static Task<T> GetOrCreateAsync<T>(this IMemoryCache cache, object key, Func<object, Task<T>> factory) => factory(key);
    }
}";

    private static string HelperProgram(string members) => Usings + @"
using Microsoft.Extensions.Caching.Memory;

class Program
{
    private readonly MyDbContext _db = new MyDbContext();
" + members + @"
}
" + MockNamespace + HelperMocks;

    private static DiagnosticResult HelperCall(int location, string helper, string query) =>
        VerifyCS.Diagnostic(NPlusOneLooperAnalyzer.HelperCallRule).WithLocation(location).WithArguments(helper, query);

    [Fact]
    public async Task HelperCall_AsyncHelperRunningQuery_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    async Task Run(List<User> orders)
    {
        foreach (var order in orders)
        {
            var customer = await {|#0:GetCustomerAsync(order.ParentId)|};
        }
    }

    private Task<User> GetCustomerAsync(int id) => _db.Users.FirstOrDefaultAsync(c => c.Id == id);");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "GetCustomerAsync", "FirstOrDefaultAsync"));
    }

    [Fact]
    public async Task HelperCall_AwaitingHelperWithBlockBody_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    async Task Run(int[] ids)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            var orders = await {|#0:LoadOrdersAsync(ids[i])|};
        }
    }

    private async Task<List<User>> LoadOrdersAsync(int parentId)
    {
        var query = _db.Users.Where(u => u.ParentId == parentId);
        return await query.ToListAsync();
    }");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "LoadOrdersAsync", "ToListAsync"));
    }

    [Fact]
    public async Task HelperCall_DirectSyncHelperWithFind_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var user = {|#0:Lookup(id)|};
        }
    }

    private User Lookup(int id) => _db.Users.Find(id);");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "Lookup", "Find"));
    }

    [Fact]
    public async Task HelperCall_TwoAndThreeLevelsDeep_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var two = {|#0:Outer(id)|};
            var three = {|#1:Level1(id)|};
        }
    }

    private int Outer(int id) => Inner(id) + 1;
    private int Inner(int id) => _db.Users.Count(u => u.ParentId == id);

    private int Level1(int id) => Level2(id);
    private int Level2(int id) => Level3(id);
    private int Level3(int id) => _db.Users.Count(u => u.Id == id);");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            HelperCall(0, "Outer", "Count"),
            HelperCall(1, "Level1", "Count"));
    }

    [Fact]
    public async Task HelperCall_FourLevelsDeep_IsQuiet()
    {
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var four = Level1(id);
        }
    }

    private int Level1(int id) => Level2(id);
    private int Level2(int id) => Level3(id);
    private int Level3(int id) => Level4(id);
    private int Level4(int id) => _db.Users.Count(u => u.Id == id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_ExtensionMethodOnContext_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    async Task Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var count = await {|#0:_db.CountChildrenAsync(id)|};
        }
    }
}

static class UserQueries
{
    public static Task<int> CountChildrenAsync(this MyDbContext db, int id) =>
        db.Users.Where(u => u.ParentId == id).CountAsync();");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "CountChildrenAsync", "CountAsync"));
    }

    [Fact]
    public async Task HelperCall_LocalFunction_ReportsCallInLoop()
    {
        var test = HelperProgram(@"
    async Task Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var exists = await {|#0:ExistsAsync(id)|};
        }

        Task<bool> ExistsAsync(int id) => _db.Users.Where(u => u.Id == id).AnyAsync();
    }");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "ExistsAsync", "AnyAsync"));
    }

    [Fact]
    public async Task HelperCall_StaticHelperTakingContextAndSealedOverride_Reports()
    {
        var test = HelperProgram(@"
    void Run(int[] ids, SealedRepository repository)
    {
        foreach (var id in ids)
        {
            var a = {|#0:UserQueries.Load(_db, id)|};
            var b = {|#1:repository.Get(id)|};
        }
    }
}

static class UserQueries
{
    public static List<User> Load(MyDbContext db, int id) => db.Users.Where(u => u.ParentId == id).ToList();
}

abstract class RepositoryBase
{
    public abstract User Get(int id);
}

sealed class SealedRepository : RepositoryBase
{
    private readonly MyDbContext _db = new MyDbContext();
    public override User Get(int id) => _db.Users.Find(id);");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            HelperCall(0, "Load", "ToList"),
            HelperCall(1, "Get", "Find"));
    }

    [Fact]
    public async Task HelperCall_HelperExecutionAfterEarlyParameterGuard_StillReports()
    {
        var test = HelperProgram(@"
    async Task Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:GetAsync(id)|};
        }
    }

    private async Task<User> GetAsync(int id)
    {
        if (id <= 0)
            return null;

        if (!await _db.Users.Where(u => u.Id == id).AnyAsync())
            return null;

        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }");

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "GetAsync", "AnyAsync"));
    }

    [Fact]
    public async Task HelperCall_CacheFactoryLambda_IsQuiet()
    {
        var test = HelperProgram(@"
    private readonly IMemoryCache _cache = null;

    async Task Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var user = await GetCachedAsync(id);
        }
    }

    private Task<User> GetCachedAsync(int id) =>
        _cache.GetOrCreateAsync(id, _ => _db.Users.FirstOrDefaultAsync(u => u.Id == id));");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_MemoizedHelpers_AreQuiet()
    {
        var test = HelperProgram(@"
    private readonly Dictionary<int, User> _users = new Dictionary<int, User>();
    private List<User> _all;
    private User _current;

    async Task Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var a = await GetViaDictionaryAsync(id);
            var b = GetAll();
            var c = await GetCurrentAsync(id);
        }
    }

    private async Task<User> GetViaDictionaryAsync(int id)
    {
        if (_users.TryGetValue(id, out var user))
            return user;

        user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        _users[id] = user;
        return user;
    }

    private List<User> GetAll() => _all ??= _db.Users.ToList();

    private async Task<User> GetCurrentAsync(int id)
    {
        if (_current != null)
            return _current;

        _current = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        return _current;
    }");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_OverridableOrInterfaceDispatch_IsQuiet()
    {
        var test = HelperProgram(@"
    void Run(int[] ids, Repository repository, IRepository contract)
    {
        foreach (var id in ids)
        {
            var a = repository.Get(id);
            var b = contract.Get(id);
            var c = Virtual(id);
        }
    }

    protected virtual User Virtual(int id) => _db.Users.Find(id);
}

interface IRepository
{
    User Get(int id);
}

class Repository : IRepository
{
    private readonly MyDbContext _db = new MyDbContext();
    public virtual User Get(int id) => _db.Users.Find(id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_QueryBuilderHelper_IsQuiet()
    {
        // A helper that only builds an IQueryable runs no query itself. Executing its result in the loop is not
        // followed back into the helper either: LC007 does not trace provenance through return values.
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var query = Children(id);
            var count = Children(id).Count();
        }
    }

    private IQueryable<User> Children(int id) => _db.Users.Where(u => u.ParentId == id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_RecursionCycles_AreQuietAndTerminate()
    {
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            var a = Ping(id);
            var b = Self(id);
        }
    }

    private int Ping(int n) => n > 0 ? Pong(n - 1) : 0;
    private int Pong(int n) => n > 0 ? Ping(n - 1) : 0;
    private int Self(int n) => n > 0 ? Self(n - 1) : 0;");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_OutsideLoopOrInLambda_IsQuiet()
    {
        var test = HelperProgram(@"
    async Task Run(int[] ids)
    {
        var user = await GetAsync(1);
        foreach (var id in ids)
        {
            Func<Task<User>> later = () => GetAsync(id);
        }
    }

    private Task<User> GetAsync(int id) => _db.Users.FirstOrDefaultAsync(u => u.Id == id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_HelperWithUnprovableSource_IsQuiet()
    {
        var test = HelperProgram(@"
    void Run(int[] ids, IQueryable<User> users)
    {
        foreach (var id in ids)
        {
            var a = CountIn(users, id);
            var b = CountInMemory(new List<User>(), id);
        }
    }

    private int CountIn(IQueryable<User> source, int id) => source.Count(u => u.ParentId == id);
    private int CountInMemory(List<User> source, int id) => source.Count(u => u.ParentId == id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_HelperWhoseQueryIsInItsOwnLoop_ReportsOnlyInsideHelper()
    {
        var test = HelperProgram(@"
    void Run(int[] ids)
    {
        foreach (var id in ids)
        {
            LoadAll(ids);
        }
    }

    private void LoadAll(int[] ids)
    {
        foreach (var id in ids)
        {
            var user = {|#0:_db.Users.Find(id)|};
        }
    }");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic("LC007").WithLocation(0).WithArguments("Find"));
    }

    [Fact]
    public async Task HelperCall_DrainAndBatchLoops_AreQuiet()
    {
        var test = HelperProgram(@"
    async Task Run()
    {
        while (await HasPendingAsync())
        {
            await ProcessNextAsync();
        }

        var lastId = 0;
        while (true)
        {
            var batch = await NextBatchAsync(lastId);
            if (batch.Count == 0)
                break;
            lastId = batch[batch.Count - 1].Id;
        }
    }

    private Task<bool> HasPendingAsync() => _db.Users.AnyAsync();
    private async Task ProcessNextAsync() => await _db.Users.Where(u => u.ParentId == 0).ToListAsync();
    private Task<List<User>> NextBatchAsync(int lastId) => _db.Users.Where(u => u.Id > lastId).OrderBy(u => u.Id).Take(100).ToListAsync();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperCall_HelperInMetadataOnly_IsQuiet()
    {
        // The helper and the EF mocks live in a referenced assembly: there is no source body to read.
        var library = CSharpCompilation.Create(
            "HelperLibrary",
            new[]
            {
                CSharpSyntaxTree.ParseText(Usings.Replace("using TestNamespace;", string.Empty) + @"
namespace TestNamespace
{
    public static class LibraryQueries
    {
        public static User Lookup(MyDbContext db, int id) => db.Users.Find(id);
    }
}
" + MockNamespace)
            },
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, default),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new System.IO.MemoryStream();
        var emit = library.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<NPlusOneLooperAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = @"
using TestNamespace;

class Program
{
    void Run(MyDbContext db, int[] ids)
    {
        foreach (var id in ids)
        {
            var user = LibraryQueries.Lookup(db, id);
        }
    }
}",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromImage(image.ToArray()));

        await test.RunAsync();
    }
}
