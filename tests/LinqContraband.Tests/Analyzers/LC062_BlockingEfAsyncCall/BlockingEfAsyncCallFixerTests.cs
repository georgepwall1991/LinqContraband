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
    private static Task VerifyFixAsync(string before, string after, bool isAsync)
    {
        return new CodeFixTest
        {
            TestCode = BlockingEfAsyncCallTests.Wrap(before, isAsync),
            FixedCode = BlockingEfAsyncCallTests.Wrap(after, isAsync)
        }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code, bool isAsync)
    {
        var source = BlockingEfAsyncCallTests.Wrap(code, isAsync);
        return new CodeFixTest { TestCode = source, FixedCode = source }.RunAsync();
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

    [Fact]
    public async Task WaitWithCancellationToken_GetsNoAwaitFix()
    {
        // Wait(timeout) returns bool; Wait(CancellationToken) is also an argument-taking
        // overload, so the fixer only rewrites the parameterless Wait().
        await VerifyNoFixAsync(@"{|LC062:db.SaveChangesAsync().Wait(ct)|};", isAsync: true);
    }

    [Theory]
    // Outside async code there is no fix: a synchronous rewrite cannot be proven equivalent, because async-only
    // interceptors (SaveChangesInterceptor, DbCommandInterceptor), async-only overrides and the evaluation of the
    // dropped arguments can all differ.
    [InlineData(@"var users = {|LC062:db.Users.ToListAsync(ct).Result|};")]
    [InlineData(@"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).GetAwaiter().GetResult()|};")]
    [InlineData(@"{|LC062:db.Database.MigrateAsync(ct).GetAwaiter().GetResult()|};")]
    [InlineData(@"var rows = {|LC062:db.Database.ExecuteSqlRawAsync(""DELETE FROM Users"", ct).Result|};")]
    [InlineData(@"var rows = {|LC062:db.Users.Where(x => x.Id > id).ExecuteDeleteAsync(ct).Result|};")]
    [InlineData(@"{|LC062:db.AddRangeAsync(new User(), new User()).Wait()|};")]
    [InlineData(@"{|LC062:db.SaveChangesAsync().Wait()|};")]
    [InlineData(@"var saved = {|LC062:audited.SaveChangesAsync(true, ct).Result|};")]
    [InlineData(@"var user = {|LC062:db.Users.FindAsync(id).Result|};")]
    [InlineData(@"Func<List<User>> load = () => {|LC062:db.Users.ToListAsync().Result|};")]
    public async Task OutsideAsyncCode_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: false);
    }

    [Theory]
    // A sync lambda in an async method: await is not allowed there, and the synchronous call is LC008's finding.
    [InlineData(@"Func<List<User>> load = () => {|LC062:db.Users.ToListAsync().Result|};")]
    // No await inside a lock.
    [InlineData(@"lock (this) { var n = {|LC062:db.Users.CountAsync().Result|}; }")]
    // Wait with a timeout returns whether the task finished.
    [InlineData(@"var done = {|LC062:db.SaveChangesAsync().Wait(TimeSpan.FromSeconds(5))|};")]
    public async Task NoSafeRewrite_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: true);
    }

    [Theory]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync() /* rationale */ .Result|};",
        @"var users = await db.Users.ToListAsync() /* rationale */;")]
    [InlineData(
        @"var saved = {|LC062:db.SaveChangesAsync().GetAwaiter() /* sync entry point */ .GetResult()|};",
        @"var saved = await db.SaveChangesAsync() /* sync entry point */;")]
    public async Task CommentsBeforeTheBlockingAccess_AreKept(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: true);
    }

    [Fact]
    public async Task SingleLineCommentBeforeTheBlockingAccess_IsKeptOnItsOwnLine()
    {
        await VerifyFixAsync(
            "var users = {|LC062:db.Users.ToListAsync() // rationale\n            .Result|};",
            "var users = await db.Users.ToListAsync() // rationale\n;",
            isAsync: true);
    }

    [Theory]
    // .Result and .Wait() throw AggregateException and await does not, so any catch clause may pick a different
    // handler after the rewrite, whatever it catches.
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (AggregateException) { }")]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (Exception ex) when (ex is AggregateException) { }")]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch { }")]
    [InlineData(@"try { {|LC062:db.SaveChangesAsync().Wait()|}; } catch (SystemException) { }")]
    [InlineData(@"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } catch (InvalidOperationException) { }")]
    [InlineData(@"try { try { var users = {|LC062:db.Users.ToListAsync().Result|}; } finally { } } catch (Exception) { }")]
    public async Task InsideATryWithACatch_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code, isAsync: true);
    }

    [Fact]
    public async Task InsideATryWithOnlyAFinally_KeepsTheAwaitFix()
    {
        await VerifyFixAsync(
            @"try { var users = {|LC062:db.Users.ToListAsync().Result|}; } finally { }",
            @"try { var users = await db.Users.ToListAsync(); } finally { }",
            isAsync: true);
    }

    [Theory]
    [InlineData(@"var saved = {|LC062:audited.SaveChangesAsync().Result|};", @"var saved = await audited.SaveChangesAsync();")]
    [InlineData(@"var saved = {|LC062:db.SaveChangesAsync(true, ct).Result|};", @"var saved = await db.SaveChangesAsync(true, ct);")]
    public async Task OverriddenOrInterceptedSaves_AreAwaited(string before, string after)
    {
        await VerifyFixAsync(before, after, isAsync: true);
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

    [Fact]
    public async Task RefLikeParameterReadAfterTheCall_GetsNoFix()
    {
        // Ref-like parameters cannot appear in async code (CS4012), so this site is in a synchronous method: no fix.
        var titles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Usings + @"
class Program
{
    void Fill(System.Span<int> buffer, ShopContext db)
    {
        var count = db.Users.CountAsync().Result;
        buffer[0] = count;
    }
}
" + BlockingEfAsyncCallTests.EfMock);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task DirectiveInsideTheBlockingExpression_OffersNoFix()
    {
        var code = @"var users = {|LC062:db.Users.ToListAsync()
#pragma warning disable CS0618
            .Result|};
#pragma warning restore CS0618";

        await VerifyNoFixAsync(code, isAsync: true);
        await VerifyNoFixAsync(code, isAsync: false);
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

        await new CodeFixTest { TestCode = source, FixedCode = source }.RunAsync();
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

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    [Fact]
    public async Task Titles_NameTheRewrite()
    {
        var asyncTitles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Wrap(@"var users = db.Users.ToListAsync().Result;", isAsync: true));
        var syncTitles = await CodeActionTitles.GetAsync(
            new BlockingEfAsyncCallAnalyzer(),
            new BlockingEfAsyncCallFixer(),
            BlockingEfAsyncCallTests.Wrap(@"var users = db.Users.ToListAsync().Result;"));

        Assert.Equal(new[] { "Await the task instead of blocking" }, asyncTitles);
        Assert.Empty(syncTitles);
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
            await new CodeFixTest
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
        var project = workspace.AddProject("Test", Microsoft.CodeAnalysis.LanguageNames.CSharp)
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
