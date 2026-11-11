using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A repository that hands out an <c>IQueryable</c> moves the query's root out of sight:
/// <c>repository.Query().Count(...)</c> in a loop is still one query per item. LC007 reads a same-project method or
/// property that returns a queryable and treats its result as EF-backed when every <c>return</c> is. It stays quiet
/// when a return is in-memory or unprovable, and when the member has no source body.
/// </summary>
public partial class NPlusOneLooperTests
{
    private static DiagnosticResult Execution(int location, string query) =>
        VerifyCS.Diagnostic("LC007").WithLocation(location).WithArguments(query);

    [Fact]
    public Task ReturnedQuery_FromMethodsAndProperties_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    private IQueryable<User> Active => _db.Users.Where(u => u.Id > 0);

    private IQueryable<User> Children(int id) => _db.Users.Where(u => u.ParentId == id);

    private static IQueryable<User> ChildrenOf(MyDbContext db, int id)
    {
        if (id < 0)
            return db.Users.Where(u => u.ParentId == 0);

        return db.Set<User>().Where(u => u.ParentId == id);
    }

    async Task Run(int[] ids, UserStore store)
    {
        foreach (var id in ids)
        {
            var a = {|#0:Children(id).Count()|};
            var b = {|#1:ChildrenOf(_db, id).ToList()|};
            var c = {|#2:Active.Count(u => u.ParentId == id)|};
            var d = {|#3:store.Query().Where(u => u.ParentId == id).ToList()|};
            var e = await {|#4:store.Recent(id).OrderBy(u => u.Id).ToListAsync()|};
            var query = store.Query();
            var f = {|#5:query.Any(u => u.Id == id)|};
        }

        var counts = ids.Select(id => {|#6:Children(id).Count()|}).ToList();
    }
}

sealed class UserStore
{
    private readonly MyDbContext _db = new MyDbContext();

    public DbSet<User> Query() => _db.Users;

    public IQueryable<User> Recent(int id) => Query().Where(u => u.ParentId == id);"),
            Execution(0, "Count"),
            Execution(1, "ToList"),
            Execution(2, "Count"),
            Execution(3, "ToList"),
            Execution(4, "ToListAsync"),
            Execution(5, "Any"),
            PerElementLambda(6, "Count", "Select"));

    [Fact]
    public Task ReturnedQuery_ThroughInterfaceWhoseImplementationsAgree_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IUserStore store)
    {
        foreach (var id in ids)
        {
            var a = {|#0:store.Query().Count(u => u.ParentId == id)|};
        }
    }
}

interface IUserStore
{
    IQueryable<User> Query();
}

class UserStore : IUserStore
{
    private readonly MyDbContext _db = new MyDbContext();
    public IQueryable<User> Query() => _db.Users;"),
            Execution(0, "Count"));

    [Fact]
    public Task ReturnedQuery_InMemoryUnprovableOrWithoutBody_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    private readonly List<User> _users = new List<User>();

    private IQueryable<User> Stored { get; set; }

    private IQueryable<User> InMemory() => _users.AsQueryable();

    private IQueryable<User> Mixed(bool cached) => cached ? _users.AsQueryable() : _db.Users;

    private IQueryable<User> Either(bool cached)
    {
        if (cached)
            return _users.AsQueryable();

        return _db.Users;
    }

    private IQueryable<User> FromParameter(IQueryable<User> source, int id) => source.Where(u => u.ParentId == id);

    private IQueryable<User> Recursive(int id) => Recursive(id - 1);

    void Run(int[] ids, IQueryable<User> users, IUserStore store, AbstractStore orphan)
    {
        foreach (var id in ids)
        {
            var a = InMemory().Count();
            var b = Mixed(id > 0).Count();
            var c = Either(id > 0).Count();
            var d = FromParameter(users, id).Count();
            var e = Stored.Count();
            var f = Recursive(id).Count();
            var g = store.Query().Count();
            var h = orphan.Query().Count();
        }
    }
}

interface IUserStore
{
    IQueryable<User> Query();
}

class DbUserStore : IUserStore
{
    private readonly MyDbContext _db = new MyDbContext();
    public IQueryable<User> Query() => _db.Users;
}

class FakeUserStore : IUserStore
{
    public IQueryable<User> Query() => new List<User>().AsQueryable();
}

abstract class AbstractStore
{
    public abstract IQueryable<User> Query();"));
}
