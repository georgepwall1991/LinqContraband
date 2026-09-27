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

    [Theory]
    [InlineData(
        @"var users = {|LC062:db.Users.ToListAsync(ct).Result|};",
        @"var users = db.Users.ToList();")]
    [InlineData(
        @"{|LC062:db.SaveChangesAsync().Wait()|};",
        @"db.SaveChanges();")]
    [InlineData(
        @"var saved = {|LC062:db.SaveChangesAsync(true, ct).Result|};",
        @"var saved = db.SaveChanges(true);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).GetAwaiter().GetResult()|};",
        @"var user = db.Users.FirstOrDefault(x => x.Id == id);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: ct).Result|};",
        @"var user = db.Users.FirstOrDefault(x => x.Id == id);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FindAsync(id).Result|};",
        @"var user = db.Users.Find(id);")]
    [InlineData(
        @"var user = {|LC062:db.Users.FindAsync(new object[] { id }, ct).AsTask().Result|};",
        @"var user = db.Users.Find(new object[] { id });")]
    [InlineData(
        @"{|LC062:db.Database.MigrateAsync(ct).GetAwaiter().GetResult()|};",
        @"db.Database.Migrate();")]
    [InlineData(
        @"var transaction = {|LC062:db.Database.BeginTransactionAsync(ct).Result|};",
        @"var transaction = db.Database.BeginTransaction();")]
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
        @"var entry = {|LC062:db.Users.AddAsync(new User(), ct).Result|};",
        @"var entry = db.Users.Add(new User());")]
    [InlineData(
        @"{|LC062:db.AddRangeAsync(new User(), new User()).Wait()|};",
        @"db.AddRange(new User(), new User());")]
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
        var test = new CodeFixTest();
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
