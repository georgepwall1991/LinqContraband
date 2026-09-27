using Microsoft.CodeAnalysis.Diagnostics;
using LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;
using LinqContraband.Tests.Extensions;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC062_BlockingEfAsyncCall.BlockingEfAsyncCallAnalyzer,
    LinqContraband.Analyzers.LC062_BlockingEfAsyncCall.BlockingEfAsyncCallFixer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace LinqContraband.Tests.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class BlockingEfAsyncCallFixerTests
{
    /// <summary>Compiles the test project as <see cref="BlockingEfAsyncCallTests.MockAssemblyName"/>, the mock EF Core's assembly.</summary>
    private sealed class MockEfCodeFixTest : CodeFixTest
    {
        public MockEfCodeFixTest()
        {
            SolutionTransforms.Add((solution, projectId) =>
                solution.WithProjectAssemblyName(projectId, BlockingEfAsyncCallTests.MockAssemblyName));
        }
    }

    private static Task VerifyFixAsync(string before, string after, bool isAsync)
    {
        return new MockEfCodeFixTest
        {
            TestCode = BlockingEfAsyncCallTests.Wrap(before, isAsync),
            FixedCode = BlockingEfAsyncCallTests.Wrap(after, isAsync)
        }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code, bool isAsync)
    {
        var source = BlockingEfAsyncCallTests.Wrap(code, isAsync);
        return new MockEfCodeFixTest { TestCode = source, FixedCode = source }.RunAsync();
    }

    [Theory]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync().Result|};",
        @"var users = await db.Users.ToListAsync();")]
    [InlineData(
        @"{|LC062:db.SaveChangesAsync().Wait()|};",
        @"await db.SaveChangesAsync();")]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).GetAwaiter().GetResult()|};",
        @"var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);")]
    [InlineData(
        @"var saved = {|LC062:db.SaveChangesAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult()|};",
        @"var saved = await db.SaveChangesAsync(ct).ConfigureAwait(false);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FindAsync(id).Result|};",
        @"var user = await db.Users.FindAsync(id);")]
    [InlineData(
        @"{|LC062:db.Users.FindAsync(id).AsTask().Wait()|};",
        @"await db.Users.FindAsync(id).AsTask();")]
    [InlineData(
        @"var count = {|LC062:db.Users.ToListAsync().Result|}.Count;",
        @"var count = (await db.Users.ToListAsync()).Count;")]
    [InlineData(
        @"var any = {|LC062:db.Users.AnyAsync(ct).Result|} && id > 0;",
        @"var any = await db.Users.AnyAsync(ct) && id > 0;")]
    [InlineData(
        @"var task = db.Users.ToListAsync(ct); var users = {|LC062:task.Result|};",
        @"var task = db.Users.ToListAsync(ct); var users = await task;")]
    [InlineData(
        @"Func<Task<int>> count = async () => {|LC062:db.Users.CountAsync().Result|};",
        @"Func<Task<int>> count = async () => await db.Users.CountAsync();")]
    public async Task InAsyncCode_AwaitsTheTask(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: true);
    }

    [Theory]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync(ct).Result|};",
        @"var users = db.Users.ToList();")]
    [InlineData(
        @"var sealedDb = new SealedShopContext(); {|LC062:sealedDb.SaveChangesAsync().Wait()|};",
        @"var sealedDb = new SealedShopContext(); sealedDb.SaveChanges();")]
    [InlineData(
        @"var internalDb = new InternalShopContext(); var saved = {|LC062:internalDb.SaveChangesAsync(true, ct).Result|};",
        @"var internalDb = new InternalShopContext(); var saved = internalDb.SaveChanges(true);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).GetAwaiter().GetResult()|};",
        @"var user = db.Users.FirstOrDefault(x => x.Id == id);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: ct).Result|};",
        @"var user = db.Users.FirstOrDefault(x => x.Id == id);")]
    [InlineData(
        @"{|LC062:db.Database.MigrateAsync(ct).GetAwaiter().GetResult()|};",
        @"db.Database.Migrate();")]
    [InlineData(
        @"var rows = {|LC062:db.Database.ExecuteSqlRawAsync(""DELETE FROM Users"", ct).Result|};",
        @"var rows = db.Database.ExecuteSqlRaw(""DELETE FROM Users"");")]
    // The token here is an element of the params array, not the token parameter: it stays.
    [InlineData(
        @"var rows = {|LC062:db.Database.ExecuteSqlRawAsync(""SELECT {0}, {1}"", id, ct).Result|};",
        @"var rows = db.Database.ExecuteSqlRaw(""SELECT {0}, {1}"", id, ct);")]
    [InlineData(
        @"var rows = {|LC062:db.Users.Where(x => x.Id > id).ExecuteDeleteAsync(ct).Result|};",
        @"var rows = db.Users.Where(x => x.Id > id).ExecuteDelete();")]
    [InlineData(
        @"var sealedDb = new SealedShopContext(); {|LC062:sealedDb.AddRangeAsync(new User(), new User()).Wait()|};",
        @"var sealedDb = new SealedShopContext(); sealedDb.AddRange(new User(), new User());")]
    [InlineData(
        @"var count = {|LC062:db.Users.ToListAsync().Result|}.Count;",
        @"var count = db.Users.ToList().Count;")]
    [InlineData(
        @"Func<List<User>> load = () => {|LC062:db.Users.ToListAsync().Result|};",
        @"Func<List<User>> load = () => db.Users.ToList();")]
    public async Task OutsideAsyncCode_CallsTheSynchronousMethod(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: false);
    }

    [Fact]
    public async Task SynchronousFix_AddsSystemLinq()
    {
        const string before = @"using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    void Run(ShopContext db)
    {
        var users = {|LC062:db.Users.ToListAsync().Result|};
    }
}
";
        const string after = @"using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    void Run(ShopContext db)
    {
        var users = db.Users.ToList();
    }
}
";
        var mock = BlockingEfAsyncCallTests.Usings + BlockingEfAsyncCallTests.EfMock;
        var test = new MockEfCodeFixTest();
        test.TestState.Sources.Add(before);
        test.TestState.Sources.Add(mock);
        test.FixedState.Sources.Add(after);
        test.FixedState.Sources.Add(mock);
        await test.RunAsync();
    }

    [Theory]
    // A sync lambda in an async method: await is not allowed there, and the synchronous call is LC008's finding.
    [InlineData(@"Func<List<User>> load = () => {|LC062:db.Users.ToListAsync().Result|};", true)]
    // No await inside a lock, and the synchronous call would be LC008's finding.
    [InlineData(@"lock (this) { var n = {|LC062:db.Users.CountAsync().Result|}; }", true)]
    // A stored task outside async code: nothing to rewrite the declaration to.
    [InlineData(@"var task = db.Users.ToListAsync(ct); var users = {|LC062:task.Result|};", false)]
    // Wait with a timeout returns whether the task finished.
    [InlineData(@"var done = {|LC062:db.SaveChangesAsync().Wait(TimeSpan.FromSeconds(5))|};", true)]
    [InlineData(@"var done = {|LC062:db.SaveChangesAsync().Wait(TimeSpan.FromSeconds(5))|};", false)]
    // The catch relies on the AggregateException that .Result throws.
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (AggregateException) { }", true)]
    [InlineData(@"try { {|LC062:db.SaveChangesAsync().Wait()|}; } catch (AggregateException) { }", false)]
    // No synchronous ForEach on IQueryable.
    [InlineData(@"{|LC062:db.Users.ForEachAsync(u => { }).Wait()|};", false)]
    // The static form has no synchronous twin in EntityFrameworkQueryableExtensions.
    [InlineData(@"var users = {|LC062:EntityFrameworkQueryableExtensions.ToListAsync(db.Users).Result|};", false)]
    public async Task NoSafeRewrite_OffersNoFix(string code, bool isAsync)
    {
        await VerifyNoFixAsync(code, isAsync);
    }

    [Theory]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync() /* rationale */ .Result|};",
        @"var users = await db.Users.ToListAsync() /* rationale */;",
        true)]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync() /* rationale */ .Result|};",
        @"var users = db.Users.ToList() /* rationale */;",
        false)]
    [InlineData(
        @"var saved = {|LC062:db.SaveChangesAsync().GetAwaiter() /* sync entry point */ .GetResult()|};",
        @"var saved = await db.SaveChangesAsync() /* sync entry point */;",
        true)]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(ct) /* first */ .ConfigureAwait(false).GetAwaiter().GetResult()|};",
        @"var user = db.Users.FirstOrDefault() /* first */;",
        false)]
    public async Task CommentsBeforeTheBlockingAccess_AreKept(string before, string after, bool isAsync)
    {
        await VerifyFixAsync(before, after, isAsync);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SingleLineCommentBeforeTheBlockingAccess_IsKeptOnItsOwnLine(bool isAsync)
    {
        var fixedCall = isAsync ? "await db.Users.ToListAsync()" : "db.Users.ToList()";
        await VerifyFixAsync(
            "var users = {|LC062:db.Users.ToListAsync() // rationale\n            .Result|};",
            "var users = " + fixedCall + " // rationale\n;",
            isAsync);
    }

    [Theory]
    // Each of these catches can observe the AggregateException that .Result and .Wait() throw.
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (Exception ex) when (ex is AggregateException) { }", true)]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (Exception ex) when (ex is AggregateException) { }", false)]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch { }", true)]
    [InlineData(@"try { {|LC062:db.SaveChangesAsync().Wait()|}; } catch { }", false)]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (SystemException) { }", false)]
    [InlineData(@"try { try { var users = {|LC062:db.Users.ToListAsync().Result|}; } finally { } } catch (Exception) { }", true)]
    public async Task CatchThatCanObserveAggregateException_OffersNoFix(string code, bool isAsync)
    {
        await VerifyNoFixAsync(code, isAsync);
    }

    [Theory]
    [InlineData(
        @"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (InvalidOperationException) { }",
        @"try { var users = await db.Users.ToListAsync(); } catch (InvalidOperationException) { }",
        true)]
    [InlineData(
        @"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (InvalidOperationException) { }",
        @"try { var users = db.Users.ToList(); } catch (InvalidOperationException) { }",
        false)]
    public async Task CatchThatCannotBeAnAggregateException_KeepsTheFix(string before, string after, bool isAsync)
    {
        await VerifyFixAsync(before, after, isAsync);
    }

    [Theory]
    // AuditedContext overrides SaveChangesAsync but inherits SaveChanges: the sync call would skip the override.
    [InlineData(@"var saved = {|LC062:audited.SaveChangesAsync().Result|};")]
    [InlineData(@"{|LC062:audited.SaveChangesAsync(ct).Wait()|};")]
    // A DbContext variable can hold an AuditedContext, which this compilation declares.
    [InlineData(@"Microsoft.EntityFrameworkCore.DbContext context = audited; var saved = {|LC062:context.SaveChangesAsync().Result|};")]
    public async Task AsyncOverrideWithoutSyncOverride_OffersNoSynchronousFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: false);
    }

    [Fact]
    public async Task AsyncOverrideWithoutSyncOverride_StillAwaitsInAsyncCode()
    {
        await VerifyFixAsync(
            @"var saved = {|LC062:audited.SaveChangesAsync().Result|};",
            @"var saved = await audited.SaveChangesAsync();",
            isAsync: true);
    }

    [Fact]
    public async Task AsyncAndSyncBothOverridden_KeepsTheSynchronousFix()
    {
        await VerifyFixAsync(
            @"var full = new FullyAuditedContext(); var saved = {|LC062:full.SaveChangesAsync().Result|};",
            @"var full = new FullyAuditedContext(); var saved = full.SaveChanges();",
            isAsync: false);
    }

    [Theory]
    // Overrides are matched by signature: SaveChanges() does not cover SaveChangesAsync(bool, CancellationToken).
    [InlineData(@"var mismatched = new MismatchedAuditedContext(); var saved = {|LC062:mismatched.SaveChangesAsync(true, ct).Result|};")]
    [InlineData(@"var mismatched = new MismatchedAuditedContext(); var saved = {|LC062:mismatched.SaveChangesAsync(ct).Result|};")]
    public async Task AsyncOverloadOverriddenWithoutItsSyncOverload_OffersNoSynchronousFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: false);
    }

    [Theory]
    [InlineData(
        @"var paired = new PairedAuditedContext(); var saved = {|LC062:paired.SaveChangesAsync(true, ct).Result|};",
        @"var paired = new PairedAuditedContext(); var saved = paired.SaveChanges(true);")]
    [InlineData(
        @"var paired = new PairedAuditedContext(); var saved = {|LC062:paired.SaveChangesAsync(ct).Result|};",
        @"var paired = new PairedAuditedContext(); var saved = paired.SaveChanges();")]
    public async Task AsyncOverloadOverriddenWithItsSyncOverload_KeepsTheSynchronousFix(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: false);
    }

    [Theory]
    // Dropping the token argument would drop the call or allocation that produces it.
    [InlineData(@"CancellationToken GetToken() => ct; var users = {|LC062:db.Users.ToListAsync(GetToken()).Result|};")]
    [InlineData(@"var users = {|LC062:db.Users.ToListAsync(new CancellationTokenSource().Token).Result|};")]
    [InlineData(@"var sources = new CancellationTokenSource[1]; var users = {|LC062:db.Users.ToListAsync(sources[0].Token).Result|};")]
    public async Task TokenArgumentWithSideEffects_OffersNoSynchronousFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: false);
    }

    [Theory]
    [InlineData(@"var users = {|LC062:db.Users.ToListAsync(CancellationToken.None).Result|};", @"var users = db.Users.ToList();")]
    [InlineData(@"var users = {|LC062:db.Users.ToListAsync(default).Result|};", @"var users = db.Users.ToList();")]
    [InlineData(@"var source = new CancellationTokenSource(); var users = {|LC062:db.Users.ToListAsync(source.Token).Result|};", @"var source = new CancellationTokenSource(); var users = db.Users.ToList();")]
    public async Task TokenArgumentWithoutSideEffects_KeepsTheSynchronousFix(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: false);
    }

    [Theory]
    // A ref or ref readonly local cannot be live across an await (CS9217).
    [InlineData(@"var numbers = new int[1]; ref int slot = ref numbers[0]; var count = {|LC062:db.Users.CountAsync().Result|}; slot = count;")]
    [InlineData(@"var numbers = new int[1]; ref readonly int view = ref numbers[0]; var count = {|LC062:db.Users.CountAsync().Result|}; var sum = view + count;")]
    public async Task RefLocalReadAfterTheCall_GetsNoAwaitFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: true);
    }

    [Fact]
    public async Task RefLocalNotReadAfterTheCall_KeepsTheAwaitFix()
    {
        await VerifyFixAsync(
            @"var numbers = new int[1]; ref int slot = ref numbers[0]; slot = 1; var count = {|LC062:db.Users.CountAsync().Result|};",
            @"var numbers = new int[1]; ref int slot = ref numbers[0]; slot = 1; var count = await db.Users.CountAsync();",
            isAsync: true);
    }

    [Theory]
    // A sealed context that overrides only the async save.
    [InlineData(@"var sealedAudited = new SealedAuditedContext(); var saved = {|LC062:sealedAudited.SaveChangesAsync().Result|};")]
    // Public unsealed receivers: another assembly can derive from them and override only the async method.
    [InlineData(@"{|LC062:db.SaveChangesAsync().Wait()|};")]
    [InlineData(@"var saved = {|LC062:db.SaveChangesAsync(true, ct).Result|};")]
    [InlineData(@"var user = {|LC062:db.Users.FindAsync(id).Result|};")]
    [InlineData(@"var user = {|LC062:db.Users.FindAsync(new object[] { id }, ct).AsTask().Result|};")]
    [InlineData(@"var entry = {|LC062:db.Users.AddAsync(new User(), ct).Result|};")]
    [InlineData(@"var transaction = {|LC062:db.Database.BeginTransactionAsync(ct).Result|};")]
    public async Task ReceiverOpenToOverrides_OffersNoSynchronousFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: false);
    }

    [Theory]
    [InlineData(@"var saved = {|LC062:db.SaveChangesAsync().Result|};", @"var saved = await db.SaveChangesAsync();")]
    [InlineData(@"var user = {|LC062:db.Users.FindAsync(id).Result|};", @"var user = await db.Users.FindAsync(id);")]
    public async Task ReceiverOpenToOverrides_StillAwaitsInAsyncCode(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: true);
    }

    [Fact]
    public async Task InternalContext_WithInternalsVisibleTo_OffersNoSynchronousFix()
    {
        var source = BlockingEfAsyncCallTests.Wrap(
                @"var internalDb = new InternalShopContext(); var saved = {|LC062:internalDb.SaveChangesAsync().Result|};")
            .Replace("\nclass Program", "\n[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Other\")]\nclass Program");
        Assert.Contains("InternalsVisibleTo", source);

        await new MockEfCodeFixTest { TestCode = source, FixedCode = source }.RunAsync();
    }

    [Fact]
    public async Task RefLikeParameterReadAfterTheCall_GetsNoAwaitFix()
    {
        // Ref-like parameters cannot appear in async code (CS4012), so this site gets the synchronous fix, never await.
        var titles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Usings + @"
class Program
{
    void Fill(System.Span<int> buffer, SealedShopContext db)
    {
        var count = db.Users.CountAsync().Result;
        buffer[0] = count;
    }
}
" + BlockingEfAsyncCallTests.EfMock,
            BlockingEfAsyncCallTests.MockAssemblyName);

        Assert.Equal(new[] { "Call Count instead of blocking" }, titles);
    }

    [Fact]
    public async Task InsideARefStruct_GetsNoAwaitFix()
    {
        // `this` can be read implicitly in a ref struct, so no member of one gets the await fix.
        var source = BlockingEfAsyncCallTests.Usings + @"
ref struct Worker
{
    static async Task Run(ShopContext db)
    {
        var count = {|LC062:db.Users.CountAsync().Result|};
        await Task.Yield();
    }
}
" + BlockingEfAsyncCallTests.EfMock;

        await new MockEfCodeFixTest { TestCode = source, FixedCode = source }.RunAsync();
    }

    [Fact]
    public async Task FixAll_FixesEveryBlockingCall()
    {
        var before = BlockingEfAsyncCallTests.Wrap(@"
        var users = {|LC062:db.Users.ToListAsync().Result|};
        {|LC062:db.SaveChangesAsync().Wait()|};", isAsync: true);
        var after = BlockingEfAsyncCallTests.Wrap(@"
        var users = await db.Users.ToListAsync();
        await db.SaveChangesAsync();", isAsync: true);

        await new MockEfCodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    [Fact]
    public async Task Titles_NameTheRewrite()
    {
        var asyncTitles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Wrap(@"var users = db.Users.ToListAsync().Result;", isAsync: true),
            BlockingEfAsyncCallTests.MockAssemblyName);
        var syncTitles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Wrap(@"var users = db.Users.ToListAsync().Result;"),
            BlockingEfAsyncCallTests.MockAssemblyName);

        Assert.Equal(new[] { "Await the task instead of blocking" }, asyncTitles);
        Assert.Equal(new[] { "Call ToList instead of blocking" }, syncTitles);
    }

    /// <summary>
    /// The fixer-coverage corpus: every shape the analyzer tests report, in async and sync code, gets a compiling
    /// fix or, where no rewrite is safe, none.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlockingEfAsyncCallTests.ReportedShapes), MemberType = typeof(BlockingEfAsyncCallTests))]
    public async Task ReportedShapes_FixCompilesOrIsWithheld(string shape)
    {
        foreach (var isAsync in new[] { true, false })
        {
            var source = BlockingEfAsyncCallTests.Wrap(shape.Replace("{|#0:", "{|LC062:"), isAsync);
            var fixedSource = await ApplyFixAsync(BlockingEfAsyncCallTests.Wrap(shape.Replace("{|#0:", "").Replace("|}", ""), isAsync));

            // The verifier compiles the fixed document and fails on any compiler error or leftover LC062.
            await new MockEfCodeFixTest
            {
                TestCode = source,
                FixedCode = fixedSource,
            }.RunAsync();
        }
    }

    /// <summary>Applies the first offered fix, or returns the source unchanged when none is offered.</summary>
    private static async Task<string> ApplyFixAsync(string source)
    {
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
        var project = workspace.AddProject(BlockingEfAsyncCallTests.MockAssemblyName, Microsoft.CodeAnalysis.LanguageNames.CSharp)
            .WithCompilationOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(references);
        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!
            .WithAnalyzers(System.Collections.Immutable.ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new BlockingEfAsyncCallAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = Assert.Single(diagnostics);

        var actions = new List<Microsoft.CodeAnalysis.CodeActions.CodeAction>();
        await new BlockingEfAsyncCallFixer().RegisterCodeFixesAsync(new Microsoft.CodeAnalysis.CodeFixes.CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None));

        if (actions.Count == 0)
        {
            var span = diagnostic.Location.SourceSpan;
            return source.Substring(0, span.Start) + "{|LC062:" + source.Substring(span.Start, span.Length) + "|}" +
                   source.Substring(span.End);
        }

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation>().Single().ChangedSolution;
        var text = await changed.GetDocument(document.Id)!.GetTextAsync();
        return text.ToString();
    }
}
