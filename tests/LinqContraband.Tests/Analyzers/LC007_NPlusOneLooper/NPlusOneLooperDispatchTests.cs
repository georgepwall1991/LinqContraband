using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A loop that calls a repository through its interface, or through a virtual or abstract method, hides the query
/// behind dynamic dispatch. LC007 resolves the call to the implementations declared in the same project and reports
/// it when every one of them runs a query. It stays quiet when the implementations disagree (an in-memory or cached
/// one), when there are none or too many, and when the interface or receiver type is not declared in the project.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string InterfaceRepository = @"
interface IRepository
{
    User Get(int id);
}

class Repository : IRepository
{
    private readonly MyDbContext _db = new MyDbContext();
    public User Get(int id) => _db.Users.Find(id);
}
";

    [Fact]
    public Task Dispatch_InterfaceWithOneImplementation_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IRepository repository)
    {
        foreach (var id in ids)
        {
            var user = {|#0:repository.Get(id)|};
        }
    }
}
" + InterfaceRepository.TrimEnd().TrimEnd('}')),
            HelperCall(0, "Get", "Find"));

    [Fact]
    public Task Dispatch_InterfaceWhoseImplementationsAllQuery_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IRepository repository)
    {
        foreach (var id in ids)
        {
            var user = {|#0:repository.Get(id)|};
        }
    }
}
" + InterfaceRepository + @"
class ArchiveRepository : IRepository
{
    private readonly MyDbContext _db = new MyDbContext();
    User IRepository.Get(int id) => _db.Users.FirstOrDefault(u => u.Id == id);"),
            HelperCall(0, "Get", "Find"));

    [Fact]
    public Task Dispatch_GenericInterfaceAndAsyncMethod_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    async Task Run(int[] ids, IStore<User> store)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:store.FindAsync(id)|};
        }
    }
}

interface IStore<T> where T : class
{
    Task<T> FindAsync(int id);
}

class EfStore<T> : IStore<T> where T : class
{
    private readonly MyDbContext _db = new MyDbContext();
    public async Task<T> FindAsync(int id) => await _db.Set<T>().FindAsync(id);"),
            HelperCall(0, "FindAsync", "FindAsync"));

    [Fact]
    public Task Dispatch_ThroughServiceAndDecorator_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, UserService service)
    {
        foreach (var id in ids)
        {
            var user = {|#0:service.Load(id)|};
        }
    }
}

sealed class UserService
{
    private readonly IRepository _repository;
    public UserService(IRepository repository) => _repository = repository;
    public User Load(int id) => _repository.Get(id);
}
" + InterfaceRepository + @"
class LoggingRepository : IRepository
{
    private readonly IRepository _inner;
    public LoggingRepository(IRepository inner) => _inner = inner;
    public User Get(int id)
    {
        Console.WriteLine(id);
        return _inner.Get(id);
    }"),
            HelperCall(0, "Load", "Find"));

    [Fact]
    public Task Dispatch_AbstractAndVirtualMethods_Report() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, ReaderBase reader, AuditedReader audited)
    {
        foreach (var id in ids)
        {
            var a = {|#0:reader.Read(id)|};
            var b = {|#1:audited.Read(id)|};
            var c = {|#2:Virtual(id)|};
        }
    }

    protected virtual User Virtual(int id) => _db.Users.Find(id);
}

abstract class ReaderBase
{
    protected readonly MyDbContext Db = new MyDbContext();
    public abstract User Read(int id);
}

class PlainReader : ReaderBase
{
    public override User Read(int id) => Db.Users.Find(id);
}

class AuditedReader : PlainReader
{
    public override User Read(int id)
    {
        Console.WriteLine(id);
        return base.Read(id);
    }"),
            HelperCall(0, "Read", "Find"),
            HelperCall(1, "Read", "Find"),
            HelperCall(2, "Virtual", "Find"));

    [Fact]
    public Task Dispatch_ReceiverTypeNarrowsTheImplementations_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, ReaderBase any, DbReader db)
    {
        foreach (var id in ids)
        {
            var a = any.Read(id);
            var b = {|#0:db.Read(id)|};
        }
    }
}

abstract class ReaderBase
{
    public abstract User Read(int id);
}

class DbReader : ReaderBase
{
    private readonly MyDbContext _db = new MyDbContext();
    public override User Read(int id) => _db.Users.Find(id);
}

class FakeReader : ReaderBase
{
    public override User Read(int id) => new User();"),
            HelperCall(0, "Read", "Find"));

    [Fact]
    public Task Dispatch_TypeParameterReceiverAndPerElementLambda_Report() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run<TRepository>(int[] ids, TRepository typed, IRepository repository) where TRepository : IRepository
    {
        foreach (var id in ids)
        {
            var user = {|#0:typed.Get(id)|};
        }

        var users = ids.Select(id => {|#1:repository.Get(id)|}).ToList();
    }
}
" + InterfaceRepository.TrimEnd().TrimEnd('}')),
            HelperCall(0, "Get", "Find"),
            PerElementLambdaHelperCall(1, "Get", "Find", "Select"));

    [Fact]
    public Task Dispatch_ImplementationsDisagree_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IRepository repository, Reader reader)
    {
        foreach (var id in ids)
        {
            var a = repository.Get(id);
            var b = reader.Read(id);
        }
    }
}
" + InterfaceRepository + @"
class InMemoryRepository : IRepository
{
    private readonly Dictionary<int, User> _users = new Dictionary<int, User>();
    public User Get(int id) => _users[id];
}

class Reader
{
    protected readonly MyDbContext Db = new MyDbContext();
    public virtual User Read(int id) => Db.Users.Find(id);
}

class PreloadedReader : Reader
{
    private readonly List<User> _users = new List<User>();
    public override User Read(int id) => _users.First(u => u.Id == id);"));

    [Fact]
    public Task Dispatch_CachingDecorator_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IRepository repository)
    {
        foreach (var id in ids)
        {
            var user = repository.Get(id);
        }
    }
}
" + InterfaceRepository + @"
class CachedRepository : IRepository
{
    private readonly IRepository _inner;
    private readonly Dictionary<int, User> _cache = new Dictionary<int, User>();
    public CachedRepository(IRepository inner) => _inner = inner;
    public User Get(int id)
    {
        if (_cache.TryGetValue(id, out var user))
            return user;

        return _cache[id] = _inner.Get(id);
    }"));

    [Fact]
    public Task Dispatch_NoImplementationOrNotDeclaredHere_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, List<User> users, IUnimplemented missing, IComparer<User> comparer, AbstractOnly orphan)
    {
        foreach (var id in ids)
        {
            var a = missing.Get(id);
            var b = comparer.Compare(users[0], users[1]);
            var c = orphan.Get(id);
        }
    }
}

interface IUnimplemented
{
    User Get(int id);
}

abstract class AbstractOnly
{
    public abstract User Get(int id);
}

class QueryingComparer : IComparer<User>
{
    private readonly MyDbContext _db = new MyDbContext();
    public int Compare(User x, User y) => _db.Users.Count(u => u.ParentId == x.Id) - _db.Users.Count(u => u.ParentId == y.Id);"));

    [Fact]
    public Task Dispatch_MoreImplementationsThanTheBudget_IsQuiet()
    {
        var implementations = string.Concat(Enumerable.Range(1, 9).Select(i => @"
class Repository" + i + @" : IRepository
{
    private readonly MyDbContext _db = new MyDbContext();
    public User Get(int id) => _db.Users.Find(id);
}
"));

        return VerifyCS.VerifyAnalyzerAsync(
            HelperProgram(@"
    void Run(int[] ids, IRepository repository)
    {
        foreach (var id in ids)
        {
            var user = repository.Get(id);
        }
    }
}

interface IRepository
{
    User Get(int id);
}
" + implementations.TrimEnd().TrimEnd('}')));
    }
}
