using System.Collections.Immutable;
using LinqContraband.Analyzers.LC009_MissingAsNoTracking;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Extensions;

/// <summary>
/// Replays the Fix All request <c>dotnet format analyzers</c> makes: it registers fixes for the rule's first finding
/// only and passes on the equivalence key of the action offered there, so a first finding without a fix means a
/// request with no key.
/// </summary>
public sealed class LinqContrabandFixAllProviderTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
            }

            public class DbSet<T> : IQueryable<T>
            {
                public System.Type ElementType => throw new System.NotImplementedException();
                public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
                public IQueryProvider Provider => throw new System.NotImplementedException();
                public IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
            }
        }

        public class User { public bool Active { get; set; } }
        public class Db { public DbSet<User> Users => null!; }

        public class Queries
        {
            // The entities leave the method, so LC009 reports this query without a fix.
            public void Escapes(Db db) => Show(db.Users.Where(u => u.Active).ToList());

            private static void Show(object value) { }

            public int CountActive(Db db)
            {
                var users = db.Users.Where(u => u.Active).ToList();
                return users.Count;
            }

            public int CountAll(Db db)
            {
                var users = db.Users.ToList();
                return users.Count;
            }
        }
        """;

    [Fact]
    public async Task FixAll_WithoutAnEquivalenceKey_FixesTheFindingsThatHaveAFix()
    {
        var (document, diagnostics) = await AnalyzeAsync();
        Assert.Equal(3, diagnostics.Length);

        var fixer = new MissingAsNoTrackingFixer();
        var context = DotnetFormatRequest(document, fixer, diagnostics);
        Assert.Null(context.CodeActionEquivalenceKey);

        // The batch fixer on its own matches no action and gives up on the whole rule.
        Assert.Null(await WellKnownFixAllProviders.BatchFixer.GetFixAsync(context));

        var action = await fixer.GetFixAllProvider().GetFixAsync(context);
        Assert.NotNull(action);

        var fixedText = await ApplyAsync(document, action!);
        Assert.Contains("public void Escapes(Db db) => Show(db.Users.Where(u => u.Active).ToList());", fixedText);
        Assert.Contains("var users = db.Users.AsNoTracking().Where(u => u.Active).ToList();", fixedText);
        Assert.Contains("var users = db.Users.AsNoTracking().ToList();", fixedText);
    }

    [Fact]
    public async Task FixAll_WithoutAnEquivalenceKey_ReturnsNothingWhenNoFindingHasAFix()
    {
        var (document, diagnostics) = await AnalyzeAsync();
        var escaping = diagnostics.Take(1).ToImmutableArray();

        var context = DotnetFormatRequest(document, new MissingAsNoTrackingFixer(), escaping);

        Assert.Null(await LinqContrabandFixAllProvider.Instance.GetFixAsync(context));
    }

    [Fact]
    public async Task FixAll_WithAnEquivalenceKey_BehavesLikeTheBatchFixer()
    {
        var (document, diagnostics) = await AnalyzeAsync();
        var fixer = new MissingAsNoTrackingFixer();
        var context = new FixAllContext(
            document, fixer, FixAllScope.Document, "AddAsNoTracking", [MissingAsNoTrackingAnalyzer.DiagnosticId], new Diagnostics(diagnostics), CancellationToken.None);

        var action = await LinqContrabandFixAllProvider.Instance.GetFixAsync(context);

        Assert.Equal(
            await ApplyAsync(document, (await WellKnownFixAllProviders.BatchFixer.GetFixAsync(context))!),
            await ApplyAsync(document, action!));
        Assert.Equal(
            WellKnownFixAllProviders.BatchFixer.GetSupportedFixAllScopes(),
            LinqContrabandFixAllProvider.Instance.GetSupportedFixAllScopes());
    }

    private static FixAllContext DotnetFormatRequest(Document document, CodeFixProvider fixer, ImmutableArray<Diagnostic> diagnostics)
    {
        CodeAction? first = null;
        var fixContext = new CodeFixContext(document, diagnostics[0], (action, _) => first ??= action, CancellationToken.None);
        fixer.RegisterCodeFixesAsync(fixContext).GetAwaiter().GetResult();

        return new FixAllContext(
            document, fixer, FixAllScope.Solution, first?.EquivalenceKey, [MissingAsNoTrackingAnalyzer.DiagnosticId], new Diagnostics(diagnostics), CancellationToken.None);
    }

    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeAsync()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Linq.dll" or "System.Linq.Expressions.dll" or "System.Collections.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("App", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(references);
        var document = project.AddDocument("Queries.cs", Source);

        var compilation = (await document.Project.GetCompilationAsync())!;
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers([new MissingAsNoTrackingAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        return (document, diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray());
    }

    private static async Task<string> ApplyAsync(Document document, CodeAction action)
    {
        var operation = (await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
        return (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    private sealed class Diagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
