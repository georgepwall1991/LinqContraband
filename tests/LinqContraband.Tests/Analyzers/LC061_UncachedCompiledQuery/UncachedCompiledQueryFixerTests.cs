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

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = WrapMembers(before), FixedCode = WrapMembers(after) }.RunAsync();
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
    }", @"
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Get(int id)
    {
        return GetQuery(_db, id);
    }");
    }

    [Fact]
    public async Task AsyncExpressionBodiedMethod_HoistsWithTheTaskType()
    {
        await VerifyFixAsync(@"
    public Task<Blog> Get(int id) => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);", @"
    private static readonly Func<Ctx, int, Task<Blog>> GetQuery = EF.CompileAsyncQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Task<Blog> Get(int id) => GetQuery(_db, id);");
    }

    [Fact]
    public async Task NullableResult_KeepsTheAnnotation()
    {
        await VerifyFixAsync(@"
#nullable enable
    public Task<Blog?> Get(int id) => {|LC061:EF.CompileAsyncQuery|}((Ctx c, int i) => (Blog?)c.Blogs.First(b => b.Id == i))(_db, id);
#nullable disable", @"
#nullable enable
    private static readonly Func<Ctx, int, Task<Blog?>> GetQuery = EF.CompileAsyncQuery((Ctx c, int i) => (Blog?)c.Blogs.First(b => b.Id == i));

    public Task<Blog?> Get(int id) => GetQuery(_db, id);
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
    }", @"
    private static readonly Func<Ctx, int> CountQuery = EF.CompileQuery((Ctx c) => c.Blogs.Count());

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
    public static Func<Ctx, int, Blog> ById => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));", @"
    private static readonly Func<Ctx, int, Blog> ByIdQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public static Func<Ctx, int, Blog> ById => ByIdQuery;");
    }

    [Fact]
    public async Task DelegateNestedInCacheFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => (c, i) => {|LC061:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);", @"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x));

    public Blog Get(string key, int id) => Cache.GetOrAdd(key, _ => (c, i) => GetQuery(c, i))(_db, id);");
    }

    [Fact]
    public async Task InstanceFieldInitializer_Hoists()
    {
        await VerifyFixAsync(@"
    private readonly Blog _first = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(new Ctx(), 1);", @"
    private static readonly Func<Ctx, int, Blog> CompiledQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    private readonly Blog _first = CompiledQuery(new Ctx(), 1);");
    }

    [Fact]
    public async Task UpdateFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, _ => null, (_, _) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id));", @"
    private static readonly ConcurrentDictionary<string, Blog> Cache = new();
    private static readonly Func<Ctx, int, Blog> LoadQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Load(string key, int id) => Cache.AddOrUpdate(key, _ => null, (_, _) => LoadQuery(_db, id));");
    }

    [Fact]
    public async Task CachedValueLambda_Hoists()
    {
        await VerifyFixAsync(@"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    public Blog Get(string key, int id) => Cache.GetOrAdd(key, (c, i) => {|LC061:EF.CompileQuery|}((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x))(c, i))(_db, id);", @"
    private static readonly ConcurrentDictionary<string, Func<Ctx, int, Blog>> Cache = new();
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c2, int x) => c2.Blogs.First(b => b.Id == x));

    public Blog Get(string key, int id) => Cache.GetOrAdd(key, (c, i) => GetQuery(c, i))(_db, id);");
    }

    [Fact]
    public async Task OverloadedPrivateFactory_Hoists()
    {
        await VerifyFixAsync(@"
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static Func<Ctx, int, Blog> Build(int unused) => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    private static readonly Func<Ctx, int, Blog> ById = Build(0);
    public Blog Get(int id) => Build()(_db, id);", @"
    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

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
    public Blog Get(int id) => new Lazy<Func<Ctx, int, Blog>>(() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))).Value(_db, id);", @"
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Get(int id) => new Lazy<Func<Ctx, int, Blog>>(() => GetQuery).Value(_db, id);");
    }

    [Fact]
    public async Task NotNullGuardedStore_Hoists()
    {
        await VerifyFixAsync(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId != null) _byId = {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }", @"
    private Func<Ctx, int, Blog> _byId;
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Get(int id) { if (_byId != null) _byId = GetQuery; return _byId(_db, id); }");
    }

    [Fact]
    public async Task FactoryWithStaticAndOrdinaryCallers_Hoists()
    {
        await VerifyFixAsync(@"
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);
    private static readonly Blog First = Build()(new Ctx(), 1);", @"
    private static readonly Func<Ctx, int, Blog> BuildQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    private static Func<Ctx, int, Blog> Build() => BuildQuery;
    public Blog Get(int id) => Build()(_db, id);
    private static readonly Blog First = Build()(new Ctx(), 1);");
    }

    [Fact]
    public async Task StaticInitializerAboveTheMember_GetsNoFix()
    {
        // First's initializer runs before a field inserted below it, so it would call Build() while BuildQuery is null.
        await VerifyNoFixAsync(@"
    private static readonly Blog First = Build()(new Ctx(), 1);
    private static Func<Ctx, int, Blog> Build() => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
    public Blog Get(int id) => Build()(_db, id);");
    }

    [Fact]
    public async Task NameInUse_PicksAnotherName()
    {
        await VerifyFixAsync(@"
    private int GetQuery;
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);", @"
    private int GetQuery;
    private static readonly Func<Ctx, int, Blog> GetQuery2 = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Get(int id) => GetQuery2(_db, id);");
    }

    [Fact]
    public async Task DocumentationComment_StaysOnTheMember()
    {
        await VerifyFixAsync(@"
    /// <summary>Loads a blog.</summary>
    public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);", @"
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    /// <summary>Loads a blog.</summary>
    public Blog Get(int id) => GetQuery(_db, id);");
    }

    [Fact]
    public async Task FixAll_GivesEachCallItsOwnField()
    {
        var fixedCode = WrapMembers(@"
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    public Blog Get(int id) => GetQuery(_db, id);

    private static readonly Func<Ctx, int> CountQuery = EF.CompileQuery((Ctx c) => c.Blogs.Count());

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
        var fixedCode = WrapMembers(@"
    private static readonly Func<Ctx, int, Blog> GetQuery = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));

    private static readonly Func<Ctx, int> GetQuery2 = EF.CompileQuery((Ctx c) => c.Blogs.Count());

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
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 2
        }.RunAsync();
    }

    [Theory]
    // The query reads a local, a parameter or the instance: a static field cannot see them.
    [InlineData(@"public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c) => c.Blogs.First(b => b.Id == id))(_db);")]
    [InlineData(@"private int _min; public Blog Get(int id) => {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i && b.Id > _min))(_db, id);")]
    [InlineData(@"public Blog Get(int id) { var min = 3; return {|LC061:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i && b.Id > min))(_db, id); }")]
    // The delegate type uses the method's type parameter.
    [InlineData(@"public T Get<T>(Func<Ctx, T> pick) where T : class => {|LC061:EF.CompileQuery|}((Ctx c) => (T)null)(_db);")]
    public async Task UnhoistableQueries_GetNoFix(string members)
    {
        await VerifyNoFixAsync(members);
    }
}
