using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC061_UncachedCompiledQuery.UncachedCompiledQueryAnalyzer,
    LinqContraband.Analyzers.LC061_UncachedCompiledQuery.UncachedCompiledQueryFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC061_UncachedCompiledQuery;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class UncachedCompiledQueryFixerTests
{
    private static string WrapMembers(string members) => UncachedCompiledQueryTests.WrapMembers(members);

    /// <summary>The fixed class: <paramref name="hoisted"/> fields first, then the context field and <paramref name="members"/>.</summary>
    private static string WrapFixed(string hoisted, string members) =>
        WrapMembers(members).Replace("{\n    private readonly Ctx _db", "{\n" + hoisted + "\n\n    private readonly Ctx _db");

    private static Task VerifyFixAsync(string before, string hoisted, string after)
    {
        return new CodeFixTest { TestCode = WrapMembers(before), FixedCode = WrapFixed(hoisted, after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = WrapMembers(code), FixedCode = WrapMembers(code) }.RunAsync();
    }

    [Fact]
    public async Task DirectInvocation_HoistsToStaticReadonlyField()
    {
        await VerifyFixAsync(@"
    public Blog Get(int id)
    {
        return {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);
    }",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Blog Get(int id)
    {
        return GetQuery(_db, id);
    }");
    }

    [Fact]
    public async Task AsyncExpressionBodiedMethod_HoistsWithTheTaskType()
    {
        await VerifyFixAsync(@"
    public Task<Blog> Get(int id) => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);",
            @"    private static readonly Func<Ctx, int, Task<Blog>> GetQuery = EF.CompileAsyncQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Task<Blog> Get(int id) => GetQuery(_db, id);");
    }

    [Fact]
    public async Task NullableResult_KeepsTheAnnotation()
    {
        static string Nullable(string code) => "#nullable enable\n" + code.Replace("\nnamespace Microsoft.EntityFrameworkCore", "\n#nullable disable\nnamespace Microsoft.EntityFrameworkCore");
        await new CodeFixTest
        {
            TestCode = Nullable(WrapMembers(@"
    public Task<Blog?> Get(int id) => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => (Blog?)c.Blogs.First(b => b.Id == i))(_db, id);")),
            FixedCode = Nullable(WrapFixed(@"    private static readonly Func<Ctx, int, Task<Blog?>> GetQuery = EF.CompileAsyncQuery((Ctx c, int i) => (Blog?)c.Blogs.First(b => b.Id == i));", @"
    public Task<Blog?> Get(int id) => GetQuery(_db, id);"))
        }.RunAsync();
    }

    [Fact]
    public async Task NullableContextDiffersFromTheTop_GetsNoFix()
    {
        await VerifyNoFixAsync(@"
#nullable enable
    public Task<Blog?> Get(int id) => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => (Blog?)c.Blogs.First(b => b.Id == i))(_db, id);
#nullable disable");
    }

    [Fact]
    public async Task LocalOnlyInvoked_KeepsTheLocal()
    {
        await VerifyFixAsync(@"
    public int Count()
    {
        var query = {|LC061:EF.CompileQuery|}((Ctx c) => c.Blogs.Count());
        return query(_db);
    }",
            @"    private static readonly Func<Ctx, int> CountQuery = EF.CompileQuery((Ctx c) => c.Blogs.Count());", @"
    public int Count()
    {
        var query = CountQuery;
        return query(_db);
    }");
    }

    [Fact]
    public async Task ExpressionBodiedProperty_Hoists()
    {
        await VerifyFixAsync(@"
    public static Func<Ctx, int, Blog> ById => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));",
            @"    private static readonly Func<Ctx, int, Blog> ByIdQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public static Func<Ctx, int, Blog> ById => ByIdQuery;");
    }

    [Fact]
    public async Task DelegateNestedInCacheFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => (c, i) => {|LC061:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x));", @"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => (c, i) => GetQuery(c, i))(_db, id);");
    }

    [Fact]
    public async Task InstanceFieldInitializer_Hoists()
    {
        await VerifyFixAsync(@"
    private readonly Blog _first = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);",
            @"    private static readonly Func<Ctx, int, Blog> CompiledQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private readonly Blog _first = CompiledQuery(new Ctx(), 1);");
    }

    [Fact]
    public async Task UpdateFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, _ => null, (_, _) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id));",
            @"    private static readonly Func<Ctx, int, Blog> LoadQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, _ => null, (_, _) => LoadQuery(_db, id));");
    }

    [Fact]
    public async Task CachedValueLambda_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, (c, i) => {|LC061:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x));", @"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, (c, i) => GetQuery(c, i))(_db, id);");
    }

    [Fact]
    public async Task OverloadedPrivateFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static Func<Ctx, int, Blog> Build(int unused) => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static readonly Func<Ctx, int, Blog> ById = Build(0);
    public Blog Get(int id) => Build()(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    private static Func<Ctx, int, Blog> Build(int unused) => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static readonly Func<Ctx, int, Blog> ById = Build(0);
    public Blog Get(int id) => Build()(_db, id);");
    }

    [Fact]
    public async Task MemberAfterConditionalDirective_GetsNoFix()
    {
        await VerifyNoFixAsync(@"
#if !LC061_EXCLUDED
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);
#endif");
    }

    [Fact]
    public async Task LazyBuiltPerCall_Hoists()
    {
        await VerifyFixAsync(@"
    public Blog Get(int id) => new Lazy<Func<Ctx, int, Blog>>(() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))).Value(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Blog Get(int id) => new Lazy<Func<Ctx, int, Blog>>(() => GetQuery).Value(_db, id);");
    }

    [Fact]
    public async Task NotNullGuardedStore_Hoists()
    {
        await VerifyFixAsync(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId != null) _byId = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId != null) _byId = GetQuery; return _byId(_db, id); }");
    }

    [Fact]
    public async Task FactoryWithStaticAndOrdinaryCallers_Hoists()
    {
        await VerifyFixAsync(@"
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);
    private static readonly Blog First = Build()(new Ctx(), 1);",
            @"    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    public Blog Get(int id) => Build()(_db, id);
    private static readonly Blog First = Build()(new Ctx(), 1);");
    }

    [Fact]
    public async Task StaticInitializerAboveTheMember_HoistsToTheTop()
    {
        // The field goes first, so First's initializer finds it set when it calls Build().
        await VerifyFixAsync(@"
    private static readonly Blog First = Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static readonly Blog First = Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    public Blog Get(int id) => Build()(_db, id);");
    }

    [Fact]
    public async Task StaticInitializerReachingTheMemberIndirectly_HoistsToTheTop()
    {
        await VerifyFixAsync(@"
    private static readonly Blog First = Wrapper();
    private static Blog Wrapper() => Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static readonly Blog First = Wrapper();
    private static Blog Wrapper() => Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    public Blog Get(int id) => Build()(_db, id);");
    }

    [Fact]
    public async Task PerCallDictionary_Hoists()
    {
        await VerifyFixAsync(@"
    public Blog Get(string key, int id) => new ConcurrentDictionary<string, Func<Ctx, int, Blog>>().GetOrAdd(key, _ => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Blog Get(string key, int id) => new ConcurrentDictionary<string, Func<Ctx, int, Blog>>().GetOrAdd(key, _ => GetQuery)(_db, id);");
    }

    private const string PartialMembers = @"
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);";

    private static string PartialRepo(string code) => (code.Contains("class Repo") ? code : WrapMembers(code)).Replace("class Repo", "partial class Repo");

    [Theory]
    [InlineData("partial class Repo { private static readonly Blog First = Build()(new Ctx(), 1); }")]
    [InlineData("partial class Repo { private static readonly int Unrelated = 3; }")]
    [InlineData("partial class Repo { private static readonly Blog First; static Repo() { First = Build()(new Ctx(), 1); } }")]
    [InlineData("partial class Repo { private static event System.Func<Ctx, int, Blog> Built = (c, i) => Build()(c, i); private static Blog Raise() => Built(new Ctx(), 1); }")]
    public async Task StaticCodeInAnotherPart_GetsNoFix(string otherPart)
    {
        var test = new CodeFixTest();
        test.TestState.Sources.Add(PartialRepo(PartialMembers));
        test.TestState.Sources.Add(otherPart);
        test.FixedState.Sources.Add(PartialRepo(PartialMembers));
        test.FixedState.Sources.Add(otherPart);
        await test.RunAsync();
    }

    [Fact]
    public async Task InstanceCodeInAnotherPart_Hoists()
    {
        const string otherPart = "partial class Repo { private readonly Blog _first = new Blog(); }";
        var test = new CodeFixTest();
        test.TestState.Sources.Add(PartialRepo(PartialMembers));
        test.TestState.Sources.Add(otherPart);
        test.FixedState.Sources.Add(PartialRepo(WrapFixed(@"    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    public Blog Get(int id) => Build()(_db, id);")));
        test.FixedState.Sources.Add(otherPart);
        await test.RunAsync();
    }

    [Fact]
    public async Task CastThenInvoke_Hoists()
    {
        await VerifyFixAsync(@"
    public Blog Get(int id) => ((Func<Ctx, int, Blog>){|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Blog Get(int id) => ((Func<Ctx, int, Blog>)GetQuery)(_db, id);");
    }

    [Fact]
    public async Task EmptyPropertyPatternTrueBranch_Hoists()
    {
        await VerifyFixAsync(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId is { }) _byId = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId is { }) _byId = GetQuery; return _byId(_db, id); }");
    }

    [Fact]
    public async Task UserDefinedCache_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly MyCache Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id));
    private sealed class MyCache { public Blog GetOrAdd(string key, Func<string, Blog> valueFactory) => valueFactory(key); }",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static readonly MyCache Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => GetQuery(_db, id));
    private sealed class MyCache { public Blog GetOrAdd(string key, Func<string, Blog> valueFactory) => valueFactory(key); }");
    }

    [Fact]
    public async Task MemberInsideAnEarlierConditionalRegion_GetsNoFix()
    {
        const string members = @"
#if FEATURE
    private readonly int _featureFlag = 1;
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);
#endif";
        var code = "#define FEATURE" + WrapMembers(members);
        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task MemberAfterAClosedConditionalRegion_Hoists()
    {
        await new CodeFixTest
        {
            TestCode = "#define FEATURE" + WrapMembers(@"
#if FEATURE
    private readonly int _featureFlag = 1;
#endif
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);"),
            FixedCode = "#define FEATURE" + WrapFixed(@"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
#if FEATURE
    private readonly int _featureFlag = 1;
#endif
    public Blog Get(int id) => GetQuery(_db, id);")
        }.RunAsync();
    }

    [Theory]
    // Cache and Lazy shapes that report because the cache is not kept in a field, property or parameter.
    [InlineData(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) { var cache = Cache; return cache.GetOrAdd(key, _ => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id); }", @"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) { var cache = Cache; return cache.GetOrAdd(key, _ => GetQuery)(_db, id); }")]
    [InlineData(@"
    public Blog Get(int id) { Func<Ctx, int, Blog> q = null; return System.Threading.LazyInitializer.EnsureInitialized(ref q, () => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id); }", @"
    public Blog Get(int id) { Func<Ctx, int, Blog> q = null; return System.Threading.LazyInitializer.EnsureInitialized(ref q, () => GetQuery)(_db, id); }")]
    [InlineData(@"
    public Lazy<Func<Ctx, int, Blog>> Get() => new Lazy<Func<Ctx, int, Blog>>(() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)));", @"
    public Lazy<Func<Ctx, int, Blog>> Get() => new Lazy<Func<Ctx, int, Blog>>(() => GetQuery);")]
    [InlineData(@"
    private static ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache => new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);", @"
    private static ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache => new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => GetQuery)(_db, id);")]
    [InlineData(@"
    private Lazy<Func<Ctx, int, Blog>> _lazy;
    public Blog Get(int id) { _lazy = new Lazy<Func<Ctx, int, Blog>>(() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); return _lazy.Value(_db, id); }", @"
    private Lazy<Func<Ctx, int, Blog>> _lazy;
    public Blog Get(int id) { _lazy = new Lazy<Func<Ctx, int, Blog>>(() => GetQuery); return _lazy.Value(_db, id); }")]
    [InlineData(@"
    private sealed class Holder { public Lazy<Func<Ctx, int, Blog>> Query; }
    public Blog Get(int id) { var holder = new Holder(); holder.Query = new Lazy<Func<Ctx, int, Blog>>(() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))); return holder.Query.Value(_db, id); }", @"
    private sealed class Holder { public Lazy<Func<Ctx, int, Blog>> Query; }
    public Blog Get(int id) { var holder = new Holder(); holder.Query = new Lazy<Func<Ctx, int, Blog>>(() => GetQuery); return holder.Query.Value(_db, id); }")]
    public async Task CacheNotKept_Hoists(string before, string after)
    {
        await VerifyFixAsync(before, @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", after);
    }

    [Fact]
    public async Task AsyncLazyBuiltPerCall_Hoists()
    {
        await new CodeFixTest
        {
            TestCode = WrapMembers(@"
    public Task<Blog> Get(int id) => new Nito.AsyncEx.AsyncLazy<Blog>(() => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id)).Task;") + UncachedCompiledQueryTests.AsyncLazyMocks,
            FixedCode = WrapFixed(@"    private static readonly Func<Ctx, int, Task<Blog>> GetQuery = EF.CompileAsyncQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    public Task<Blog> Get(int id) => new Nito.AsyncEx.AsyncLazy<Blog>(() => GetQuery(_db, id)).Task;") + UncachedCompiledQueryTests.AsyncLazyMocks
        }.RunAsync();
    }

    [Fact]
    public async Task StoreThroughBase_Hoists()
    {
        await new CodeFixTest
        {
            TestCode = UncachedCompiledQueryTests.WrapDerived(@"
    public Blog Get(int id) { base._query = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return base._query(_db, id); }"),
            FixedCode = UncachedCompiledQueryTests.WrapDerived(@"
    public Blog Get(int id) { base._query = GetQuery; return base._query(_db, id); }").Replace(
                "class Repo : RepoBase\n{\n", "class Repo : RepoBase\n{\n    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));\n\n")
        }.RunAsync();
    }

    [Fact]
    public async Task DirectiveBeforeTheFirstMember_GetsNoFix()
    {
        // The field would go in front of `#nullable enable`, outside the context the first member opted into.
        var code = WrapMembers(@"
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);").Replace(
            "{\n    private readonly Ctx _db", "{\n#nullable enable\n    private readonly Ctx _db");
        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task NameInUse_PicksAnotherName()
    {
        await VerifyFixAsync(@"
    private int GetQuery;
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery2 = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private int GetQuery;
    public Blog Get(int id) => GetQuery2(_db, id);");
    }

    [Fact]
    public async Task DocumentationComment_StaysOnTheMember()
    {
        await VerifyFixAsync(@"
    /// <summary>Loads a blog.</summary>
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);",
            @"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    /// <summary>Loads a blog.</summary>
    public Blog Get(int id) => GetQuery(_db, id);");
    }

    [Fact]
    public async Task FixAll_GivesEachCallItsOwnField()
    {
        var fixedCode = WrapFixed(@"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    private static readonly Func<Ctx, int> CountQuery = EF.CompileQuery((Ctx c) => c.Blogs.Count());", @"
    public Blog Get(int id) => GetQuery(_db, id);

    public int Count() => CountQuery(_db);");
        await new CodeFixTest
        {
            TestCode = WrapMembers(@"
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);

    public int Count() => {|LC061:EF.CompileQuery|}((Ctx c) => c.Blogs.Count())(_db);"),
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2
        }.RunAsync();
    }

    [Fact]
    public async Task TwoCallsInOneMember_AreNumbered()
    {
        var fixedCode = WrapFixed(@"    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    private static readonly Func<Ctx, int> GetQuery2 = EF.CompileQuery((Ctx c) => c.Blogs.Count());", @"
    public int Get(int id)
    {
        var blog = GetQuery(_db, id);
        return blog.Id + GetQuery2(_db);
    }");
        await new CodeFixTest
        {
            TestCode = WrapMembers(@"
    public int Get(int id)
    {
        var blog = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);
        return blog.Id + {|LC061:EF.CompileQuery|}((Ctx c) => c.Blogs.Count())(_db);
    }"),
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2
        }.RunAsync();
    }

    [Theory]
    // The query reads a local, a parameter or the instance: a static field cannot see them.
    [InlineData(@"public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c) => c.Blogs.First(b => b.Id == id))(_db);")]
    [InlineData(@"private int _min; public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i && b.Id > _min))(_db, id);")]
    [InlineData(@"public Blog Get(int id) { var min = 3; return {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i && b.Id > min))(_db, id); }")]
    // The query reads a static member of the type, whose order against the new first field is not known.
    [InlineData(@"private static readonly int MinId = 3; public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i && b.Id > MinId))(_db, id);")]
    // The delegate type uses the method's type parameter.
    [InlineData(@"public T Get<T>(Func<Ctx, T> pick) where T : class => {|LC061:EF.CompileQuery|}((Ctx c) => (T)null)(_db);")]
    public async Task UnhoistableQueries_GetNoFix(string members)
    {
        await VerifyNoFixAsync(members);
    }
}
