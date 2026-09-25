using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC008_SyncBlocker;

/// <summary>
/// In-memory collections wrapped with AsQueryable() run on LINQ to Objects. A sync terminal on
/// them does no I/O, and the fixer's CountAsync/ToListAsync would throw at run time because
/// EnumerableQuery does not implement IAsyncEnumerable.
/// </summary>
public class SyncBlockerInMemoryQueryTests
{
    private const string Usings = SyncBlockerEdgeCasesTests.Usings;
    private const string MockNamespace = SyncBlockerEdgeCasesTests.MockNamespace;

    [Fact]
    public async Task TestInnocent_DirectAsQueryableOverList_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    async Task<int> Main(List<User> users)
    {
        await Task.Delay(1);
        return users.AsQueryable().Where(u => u.Id > 1).Count();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalComposedAcrossConditionalReassignments_NoDiagnostic()
    {
        // Skoruba Duende admin, ConfigurationIssuesRepository.GetIssuesAsync.
        var test = Usings + @"
class Program
{
    async Task<List<User>> LoadAll() { await Task.Delay(1); return new List<User>(); }

    async Task<(int, List<User>)> GetIssuesAsync(bool x, bool y, int page, int size)
    {
        var allIssues = await LoadAll();
        var filteredQuery = allIssues.AsQueryable();
        if (x) filteredQuery = filteredQuery.Where(i => i.Id > 1);
        if (y) filteredQuery = filteredQuery.Where(i => i.Id < 100);
        var totalCount = filteredQuery.Count();
        filteredQuery = filteredQuery.Skip(page * size).Take(size);
        var list = filteredQuery.ToList();
        return (totalCount, list);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InstanceHelperReturningItsParameter_NoDiagnostic()
    {
        // Duende IdentityServer PersistedGrantStore: the same filter helper runs over the EF set
        // (async) and later over the materialized array (sync, in memory).
        var test = Usings + @"
class Store
{
    private readonly MyDbContext Context = new MyDbContext();

    async Task<IEnumerable<User>> GetAllAsync(int filter)
    {
        var grants = (await Filter(Context.Users.AsQueryable(), filter).ToListAsync()).ToArray();
        grants = Filter(grants.AsQueryable(), filter).ToArray();
        return grants;
    }

    private IQueryable<User> Filter(IQueryable<User> query, int filter)
    {
        if (filter > 0)
        {
            query = query.Where(x => x.Id == filter);
        }

        return query;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_StaticHelpersOverAsQueryableList_NoDiagnostic()
    {
        // Ombi: list.AsQueryable() passed through a static helper, including a helper that calls another.
        var test = Usings + @"
static class Paging
{
    public static IQueryable<T> Page<T>(IQueryable<T> source, int skip, int take) => source.Skip(skip).Take(take);

    public static IQueryable<User> Newest(IQueryable<User> source, int take)
    {
        var ordered = source.OrderByDescending(u => u.Id);
        return Page(ordered, 0, take);
    }
}

class Program
{
    async Task<List<User>> Main(List<User> users)
    {
        await Task.Delay(1);
        var first = Paging.Page(users.AsQueryable(), 0, 10).ToList();
        return Paging.Newest(users.AsQueryable(), 5).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalReassignedToDbSet_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    async Task<int> Main(List<User> users, bool live)
    {
        var db = new MyDbContext();
        var query = users.AsQueryable();
        if (live) query = db.Users;
        await Task.Delay(1);
        return {|LC008:query.Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalReassignedInsideLambda_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    async Task<int> Main(List<User> users)
    {
        var db = new MyDbContext();
        var query = users.AsQueryable();
        Action swap = () => query = db.Users;
        swap();
        await Task.Delay(1);
        return {|LC008:query.Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalPassedByRef_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    static void Swap(ref IQueryable<User> q, MyDbContext db) => q = db.Users;

    async Task<int> Main(List<User> users)
    {
        var db = new MyDbContext();
        var query = users.AsQueryable();
        Swap(ref query, db);
        await Task.Delay(1);
        return {|LC008:query.Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalCoalesceAssignedFromDbSet_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    async Task<int> Main(List<User> users)
    {
        var db = new MyDbContext();
        IQueryable<User> query = null;
        query ??= db.Users;
        await Task.Delay(1);
        return {|LC008:query.Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HelperCalledWithEfQuery_StillTriggers()
    {
        var test = Usings + @"
class Store
{
    private readonly MyDbContext Context = new MyDbContext();

    async Task<User[]> GetAllAsync(int filter)
    {
        await Task.Delay(1);
        return {|LC008:Filter(Context.Users, filter).ToArray()|};
    }

    private IQueryable<User> Filter(IQueryable<User> query, int filter)
    {
        if (filter > 0) query = query.Where(x => x.Id == filter);
        return query;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HelperIgnoringParameterAndReturningDbSet_StillTriggers()
    {
        var test = Usings + @"
class Store
{
    private readonly MyDbContext Context = new MyDbContext();

    async Task<List<User>> GetAllAsync(List<User> users)
    {
        await Task.Delay(1);
        return {|LC008:Filter(users.AsQueryable(), 1).ToList()|};
    }

    private IQueryable<User> Filter(IQueryable<User> query, int filter)
    {
        if (filter > 0) return Context.Users.Where(x => x.Id == filter);
        return query;
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HelperReturningOtherQueryableParameter_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    static IQueryable<User> Pick(IQueryable<User> local, IQueryable<User> live) => live.Where(u => u.Id > 0);

    async Task<List<User>> Main(List<User> users)
    {
        var db = new MyDbContext();
        await Task.Delay(1);
        return {|LC008:Pick(users.AsQueryable(), db.Users).ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_VirtualHelper_StillTriggers()
    {
        // An override could hand back an EF query; only non-overridable helpers are followed.
        var test = Usings + @"
class Store
{
    async Task<List<User>> GetAllAsync(List<User> users)
    {
        await Task.Delay(1);
        return {|LC008:Filter(users.AsQueryable()).ToList()|};
    }

    protected virtual IQueryable<User> Filter(IQueryable<User> query) => query;
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_RepositoryQueryRootedInDbSet_StillTriggers()
    {
        var test = Usings + @"
class Repository
{
    private readonly MyDbContext db = new MyDbContext();
    public IQueryable<User> Query() => db.Users;
}

class Program
{
    async Task<List<User>> Main(Repository repo)
    {
        await Task.Delay(1);
        return {|LC008:repo.Query().ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InterfaceTypedSequenceOfNonEntityAsQueryable_NoDiagnostic()
    {
        // VirtoCommerce: AllRegisteredSettings is IEnumerable<SettingDescriptor>, and no DbContext has a
        // DbSet<SettingDescriptor>, so it cannot be a DbSet at run time.
        var test = Usings + @"
class SettingDescriptor { public string Name { get; set; } public bool IsHidden { get; set; } }
class SettingsManager { public IEnumerable<SettingDescriptor> AllRegisteredSettings { get; } = new List<SettingDescriptor>(); }
class Program
{
    async Task<List<string>> Main(SettingsManager manager)
    {
        await Task.Delay(1);
        var query = manager.AllRegisteredSettings.AsQueryable();
        query = query.Where(x => !x.IsHidden);
        var total = query.Count();
        return query.Select(x => x.Name).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_InterfaceTypedSequenceAsQueryable_StillTriggers()
    {
        // IEnumerable<T> may be a DbSet at run time, so AsQueryable() over it is not proven in-memory.
        var test = Usings + @"
class Program
{
    async Task<List<User>> Main(IEnumerable<User> users)
    {
        await Task.Delay(1);
        var query = users.AsQueryable();
        return {|LC008:query.ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ParameterQueryable_StillTriggers()
    {
        var test = Usings + @"
class Program
{
    async Task<int> Main(IQueryable<User> users)
    {
        await Task.Delay(1);
        var query = users.Where(u => u.Id > 0);
        return {|LC008:query.Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
    [Fact]
    public async Task TestInnocent_SourceExtensionHelperAndConditional_NoDiagnostic()
    {
        var test = Usings + @"
static class UserQueries
{
    public static IQueryable<User> Active(this IQueryable<User> source, bool all) => all ? source : source.Where(u => u.Id > 0);
}

class Program
{
    async Task<int> Main(List<User> users, User[] archived, bool useArchive)
    {
        await Task.Delay(1);
        var query = useArchive ? archived.AsQueryable() : users.AsQueryable().Active(false);
        return query.Count();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SourceExtensionHelperReturningDbSet_StillTriggers()
    {
        var test = Usings + @"
static class UserQueries
{
    public static MyDbContext Db = new MyDbContext();
    public static IQueryable<User> Live(this IQueryable<User> source) => Db.Users;
}

class Program
{
    async Task<int> Main(List<User> users)
    {
        await Task.Delay(1);
        return {|LC008:users.AsQueryable().Live().Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MutuallyRecursiveHelpers_StillTriggersWithoutLooping()
    {
        var test = Usings + @"
static class Loop
{
    public static IQueryable<User> A(IQueryable<User> q, int n) => n > 0 ? B(q, n - 1) : q;
    public static IQueryable<User> B(IQueryable<User> q, int n) => A(q, n);
}

class Program
{
    async Task<int> Main(List<User> users)
    {
        await Task.Delay(1);
        return {|LC008:Loop.A(users.AsQueryable(), 3).Count()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
