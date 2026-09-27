using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC061_UncachedCompiledQuery.UncachedCompiledQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC061_UncachedCompiledQuery;

public class UncachedCompiledQueryTests
{
    private const string Usings = @"
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
";

    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    using System;
    using System.Linq.Expressions;
    using System.Threading.Tasks;

    public class DbContext { }

    public static class EF
    {
        public static Func<TContext, TResult> CompileQuery<TContext, TResult>(Expression<Func<TContext, TResult>> queryExpression)
            where TContext : DbContext => null;

        public static Func<TContext, TParam1, TResult> CompileQuery<TContext, TParam1, TResult>(Expression<Func<TContext, TParam1, TResult>> queryExpression)
            where TContext : DbContext => null;

        public static Func<TContext, Task<TResult>> CompileAsyncQuery<TContext, TResult>(Expression<Func<TContext, TResult>> queryExpression)
            where TContext : DbContext => null;

        public static Func<TContext, TParam1, Task<TResult>> CompileAsyncQuery<TContext, TParam1, TResult>(Expression<Func<TContext, TParam1, TResult>> queryExpression)
            where TContext : DbContext => null;
    }
}

namespace Other
{
    using System;
    using System.Linq.Expressions;

    public static class EF
    {
        public static Func<TContext, TParam1, TResult> CompileQuery<TContext, TParam1, TResult>(Expression<Func<TContext, TParam1, TResult>> queryExpression) => null;
    }
}

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class Ctx : Microsoft.EntityFrameworkCore.DbContext
{
    public IQueryable<Blog> Blogs { get; set; }
}
";

    /// <summary>Wraps class members in <c>class Repo</c>, which holds a context in <c>_db</c>.</summary>
    internal static string WrapMembers(string members) => Usings + @"
class Repo
{
    private readonly Ctx _db = new Ctx();
" + members + @"
}
" + EfMock;

    /// <summary>Wraps statements in <c>Repo.Run(int id)</c>.</summary>
    internal static string Wrap(string body) => WrapMembers(@"
    object Run(int id)
    {
" + body + @"
    }");

    [Theory]
    // Invoked directly.
    [InlineData(@"return {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);")]
    [InlineData(@"return {|#0:EF.CompileAsyncQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);")]
    [InlineData(@"return {|#0:EF.CompileQuery|}((Ctx c) => c.Blogs.Count())(_db);")]
    [InlineData(@"return {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)).Invoke(_db, id);")]
    [InlineData(@"return ({|#0:Microsoft.EntityFrameworkCore.EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);")]
    // Assigned to a local that is only invoked.
    [InlineData(@"var query = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return query(_db, id);")]
    [InlineData(@"Func<Ctx, int, Blog> query = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return query.Invoke(_db, id);")]
    [InlineData(@"Func<Ctx, int, Blog> query; query = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return query(_db, id);")]
    // Inside a lambda or a local function in the method.
    [InlineData(@"return Enumerable.Range(0, id).Select(i => {|#0:EF.CompileQuery|}((Ctx c, int x) => c.Blogs.First(b => b.Id == x))(_db, i)).ToList();")]
    [InlineData(@"Blog Load(int x) => {|#0:EF.CompileQuery|}((Ctx c, int y) => c.Blogs.First(b => b.Id == y))(_db, x); return Load(id);")]
    public async Task CompiledOnEveryCall_Reports(string body)
    {
        var name = body.Contains("CompileAsyncQuery") ? "CompileAsyncQuery" : "CompileQuery";
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0).WithArguments(name));
    }

    [Theory]
    // Expression-bodied members run on every call.
    [InlineData(@"public Task<Blog> Get(int id) => {|#0:EF.CompileAsyncQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);")]
    [InlineData(@"public static Func<Ctx, int, Blog> ById => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"public static Func<Ctx, int, Blog> ById { get { return {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); } }")]
    [InlineData(@"public Repo(int id) { var blog = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id); }")]
    // A private factory whose every caller invokes the result straight away.
    [InlineData(@"
    private static Func<Ctx, int, Blog> Build() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);")]
    // Stored on the instance from an ordinary method, so every call compiles again.
    [InlineData(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { _byId = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    // An instance field or property initializer runs once per instance, like a constructor.
    [InlineData(@"private readonly Blog _first = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);")]
    [InlineData(@"public Blog First { get; } = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);")]
    // A static expression-bodied property or getter still runs on every read.
    [InlineData(@"public static Blog First => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);")]
    // The cache stores the inner delegate, which compiles again on every call.
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => (c, i) => {|#0:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);")]
    [InlineData(@"
    private static readonly Lazy<Func<Ctx, int, Blog>> ById = new(() => (c, i) => {|#0:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i));")]
    // AddOrUpdate's update factory runs every time the key exists.
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, _ => null, (_, _) => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id));")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, (Blog)null, (_, _) => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id));")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, updateValueFactory: (_, _) => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id), addValueFactory: _ => null);")]
    // A lambda passed as the cached value itself is what the cache keeps, and it compiles on every call.
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, (c, i) => {|#0:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);")]
    // Overloads are told apart by symbol: Build() is only invoked, Build(int) initializes a static field.
    [InlineData(@"
    private static Func<Ctx, int, Blog> Build() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static Func<Ctx, int, Blog> Build(int unused) => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static readonly Func<Ctx, int, Blog> ById = Build(0);
    public Blog Get(int id) => Build()(_db, id);")]
    // A guard that tests the member is not null, or tests another member, is not a lazy cache.
    [InlineData(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId != null) _byId = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    [InlineData(@"
    private Func<Ctx, int, Blog> _byId;
    private Func<Ctx, int, Blog> _other;
    public Blog Get(int id) { if (_other == null) _byId = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    [InlineData(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId is null) { } else { _byId = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); } return _byId(_db, id); }")]
    // A private factory with any ordinary caller still compiles on every call, even if a static initializer calls it too.
    [InlineData(@"
    private static readonly Blog First = Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);")]
    // A Lazy built and read on every call.
    [InlineData(@"public Blog Get(int id) => new Lazy<Func<Ctx, int, Blog>>(() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))).Value(_db, id);")]
    [InlineData(@"public Blog Get(int id) { var lazy = new Lazy<Func<Ctx, int, Blog>>(() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); return lazy.Value(_db, id); }")]
    [InlineData(@"public Blog Get(int id) => new Lazy<Blog>(() => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id)).Value;")]
    public async Task CompiledOnEveryCall_Members_Reports(string members)
    {
        var name = members.Contains("CompileAsyncQuery") ? "CompileAsyncQuery" : "CompileQuery";
        await VerifyCS.VerifyAnalyzerAsync(WrapMembers(members), VerifyCS.Diagnostic().WithLocation(0).WithArguments(name));
    }

    [Theory]
    // The documented pattern: a static readonly field.
    [InlineData(@"private static readonly Func<Ctx, int, Blog> ById = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"private static readonly Func<Ctx, int, Task<Blog>> ById = EF.CompileAsyncQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"public static Func<Ctx, int, Blog> ById { get; } = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    // Assigned to a static member, in a static constructor or lazily.
    [InlineData(@"private static readonly Func<Ctx, int, Blog> ById; static Repo() { ById = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); }")]
    [InlineData(@"private static Func<Ctx, int, Blog> _byId; public Blog Get(int id) { _byId ??= EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    [InlineData(@"private static Func<Ctx, int, Blog> _byId; public Blog Get(int id) { if (_byId == null) _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    // Instance fields and properties: whether the instance lives long is not known (non-goal).
    [InlineData(@"private readonly Func<Ctx, int, Blog> _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"public Func<Ctx, int, Blog> ById { get; } = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"private readonly Func<Ctx, int, Blog> _byId; public Repo() { _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); }")]
    [InlineData(@"private Func<Ctx, int, Blog> _byId; public Blog Get(int id) { _byId ??= EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    [InlineData(@"private Func<Ctx, int, Blog> _byId; public Blog Get(int id) { if (_byId is null) { _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); } return _byId(_db, id); }")]
    // Lazy and static caches: the factory runs once (per key).
    [InlineData(@"private static readonly Lazy<Func<Ctx, int, Blog>> ById = new Lazy<Func<Ctx, int, Blog>>(() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)));")]
    [InlineData(@"private static readonly Lazy<Func<Ctx, int, Blog>> ById = new(() => { var q = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return q; });")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => { var q = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return q; })(_db, id);")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public void Add(string key) { Cache[key] = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); }")]
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public void Add(string key) { Cache.TryAdd(key, EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); }")]
    // A factory lambda: whoever calls it decides how long the delegate lives.
    [InlineData(@"private static readonly Func<Func<Ctx, int, Blog>> Factory = () => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    // A factory method used to initialise a static field, or one callers may cache.
    [InlineData(@"
    private static readonly Func<Ctx, int, Blog> ById = Build();
    private static Func<Ctx, int, Blog> Build() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"
    private static readonly Func<Ctx, int, Blog> ById;
    static Repo() { ById = Build(); }
    private static Func<Ctx, int, Blog> Build() { return EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); }
    public Blog Get(int id) => Build()(_db, id);")]
    [InlineData(@"public static Func<Ctx, int, Blog> Build() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"private static Func<Ctx, int, Blog> Build() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    // A local that escapes (returned, stored or passed on).
    [InlineData(@"private static Func<Ctx, int, Blog> _byId; public void Init() { var q = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); _byId = q; }")]
    [InlineData(@"public void Init(Action<Func<Ctx, int, Blog>> register) { var q = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); register(q); }")]
    // Passed straight to another method.
    [InlineData(@"public void Init(Action<Func<Ctx, int, Blog>> register) { register(EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); }")]
    // A static constructor runs once.
    [InlineData(@"private static readonly Blog First; static Repo() { First = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1); }")]
    // A static field or static auto-property initializer runs once.
    [InlineData(@"private static readonly Blog First = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);")]
    [InlineData(@"public static Blog First { get; } = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);")]
    [InlineData(@"private static readonly int Count = EF.CompileQuery((Ctx c) => c.Blogs.Count()).Invoke(new Ctx());")]
    // AddOrUpdate's add factory runs once per key.
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.AddOrUpdate(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)), (_, old) => old)(_db, id);")]
    // Null guards on the member itself.
    [InlineData(@"private Func<Ctx, int, Blog> _byId; public Blog Get(int id) { if (this._byId == null) _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    [InlineData(@"private Func<Ctx, int, Blog> _byId; public Blog Get(int id) { if (_byId != null) { } else { _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); } return _byId(_db, id); }")]
    [InlineData(@"private Func<Ctx, int, Blog> _byId; public Blog Get(int id) { if (!(_byId is not null) && id > 0) _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }")]
    // A private factory called only from static initializers or a static constructor runs once.
    [InlineData(@"
    private static readonly Blog First = Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));")]
    [InlineData(@"
    private static readonly Blog First;
    static Repo() { First = Build()(new Ctx(), 1); }
    private static Func<Ctx, int, Blog> Build() { return EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); }")]
    // A Lazy kept in an instance field, or handed back to the caller.
    [InlineData(@"private readonly Lazy<Func<Ctx, int, Blog>> _byId = new(() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)));")]
    [InlineData(@"public Lazy<Func<Ctx, int, Blog>> Make() => new Lazy<Func<Ctx, int, Blog>>(() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)));")]
    [InlineData(@"public Blog Get(int id) { var lazy = new Lazy<Func<Ctx, int, Blog>>(() => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); Keep(lazy); return lazy.Value(_db, id); } private void Keep(object o) { }")]
    // Some other EF class.
    [InlineData(@"public Blog Get(int id) => Other.EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);")]
    public async Task CachedOrUnknownLifetime_DoesNotReport(string members)
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembers(members));
    }

    [Fact]
    public async Task TopLevelStatements_DoNotReport()
    {
        var code = Usings + @"
var blog = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);
Console.WriteLine(blog.Id);
" + EfMock;
        await new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC061_UncachedCompiledQuery.UncachedCompiledQueryAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestState = { Sources = { code }, OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication }
        }.RunAsync();
    }

    [Fact]
    public async Task StaticCompiledQueriesClass_DoesNotReport()
    {
        var code = Usings + @"
static class Queries
{
    public static readonly Func<Ctx, int, Blog> ById = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
}

class Repo
{
    private readonly Ctx _db = new Ctx();
    public Blog Get(int id) => Queries.ById(_db, id);
}
" + EfMock;
        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}
