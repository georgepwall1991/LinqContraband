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
