using System.Collections.Immutable;
using LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Runs LC062 and its fixes against the real EF Core 8 assemblies (copied to efcore/ by the test project), so the
/// API identification and the synchronous counterparts are checked against EF Core's own signatures, not mocks.
/// </summary>
public class BlockingEfAsyncCallRealEfCoreTests
{
    private const string Prelude = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } = """"; }

public class ShopContext : DbContext
{
    public DbSet<User> Users => Set<User>();
}
";

    private const string Body = @"
        var users = db.Users.ToListAsync(ct).Result;
        var first = db.Users.FirstOrDefaultAsync(u => u.Id == id, ct).GetAwaiter().GetResult();
        var count = db.Users.CountAsync(ct).Result;
        var found = db.Users.FindAsync(id).Result;
        var foundWithToken = db.Users.FindAsync(new object[] { id }, ct).AsTask().Result;
        var entry = db.Users.AddAsync(new User(), ct).Result;
        db.AddRangeAsync(new User(), new User()).Wait();
        var saved = db.SaveChangesAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult();
        var transaction = db.Database.BeginTransactionAsync(ct).Result;
        var created = db.Database.EnsureCreatedAsync(ct).Result;
        db.Database.MigrateAsync(ct).Wait();
        var rows = db.Database.ExecuteSqlRawAsync(""DELETE FROM Users"", ct).Result;
        var deleted = db.Users.Where(u => u.Id > id).ExecuteDeleteAsync(ct).Result;
        var updated = db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.Name, ""x""), ct).Result;
        var interpolated = db.Database.ExecuteSqlInterpolatedAsync($""DELETE FROM Users WHERE Id = {id}"", ct).Result;
        var inMemory = new List<User>().AsQueryable().ToListAsync(ct);
        var awaitedLater = db.Users.ToListAsync(ct);
";

    private static string Source(bool isAsync) => Prelude + @"
public class Program
{
    public " + (isAsync ? "async Task" : "void") + @" Run(ShopContext db, int id, CancellationToken ct)
    {" + Body + (isAsync ? "        await awaitedLater;\n        var fromAwaited = awaitedLater.Result;\n" : "") + @"
    }
}
";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryEfCoreAsyncFamily_ReportsOnce(bool isAsync)
    {
        var document = CreateDocument(Source(isAsync));
        var diagnostics = await GetLc062Async(document);

        // 15 blocking calls on EF Core tasks; the in-memory query and the awaited task stay quiet.
        Assert.Equal(15, diagnostics.Length);
    }

    [Theory]
    [InlineData(false, "ToList", "ExecuteSqlRaw(\"DELETE FROM Users\")", "Find(new object[] { id })")]
    [InlineData(true, "await db.Users.ToListAsync(ct)", "await db.Database.MigrateAsync(ct)", "await db.SaveChangesAsync(ct).ConfigureAwait(false)")]
    public async Task EveryFix_CompilesAgainstEfCore(bool isAsync, params string[] expectedFragments)
    {
        var document = CreateDocument(Source(isAsync));
        var errorsBefore = await CountErrorsAsync(document);
        Assert.Equal(0, errorsBefore);

        // Apply one fix at a time until no diagnostic has a fix left.
        for (var round = 0; round < 32; round++)
        {
            var diagnostics = await GetLc062Async(document);
            CodeAction? action = null;
            foreach (var diagnostic in diagnostics)
            {
                var actions = new List<CodeAction>();
                await new BlockingEfAsyncCallFixer().RegisterCodeFixesAsync(
                    new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None));
                if (actions.Count > 0)
                {
                    action = actions[0];
                    break;
                }
            }

            if (action == null)
                break;

            var operations = await action.GetOperationsAsync(CancellationToken.None);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            Assert.Equal(0, await CountErrorsAsync(document));
        }

        var text = (await document.GetTextAsync()).ToString();
        foreach (var fragment in expectedFragments)
            Assert.True(text.Contains(fragment, StringComparison.Ordinal), text);

        // Every EF Core family has a synchronous counterpart or await, so nothing is left.
        Assert.Empty(await GetLc062Async(document));
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("RealEfCore", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithMetadataReferences(GetMetadataReferences());
        return project.AddDocument("Test.cs", source);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetLc062Async(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        return await compilation!
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new BlockingEfAsyncCallAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: (exception, _, _) => throw exception,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<int> CountErrorsAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        return compilation!.GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            yield return MetadataReference.CreateFromFile(path);

        // Copied by the test project's CopyEfCoreTestReferences target.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "efcore"), "*.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }
}
