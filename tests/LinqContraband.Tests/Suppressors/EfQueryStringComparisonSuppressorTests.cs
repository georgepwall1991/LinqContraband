using System.Collections.Immutable;
using LinqContraband.Suppressors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Suppressors;

/// <summary>
/// Runs <see cref="EfQueryStringComparisonSuppressor"/> over a real EF Core compilation. The CA rules ship with the
/// SDK rather than as a test dependency, so <see cref="FakeCultureAnalyzer"/> reports their ids at the same
/// locations the real rules use (checked against the .NET 10 SDK analyzers).
/// </summary>
public class EfQueryStringComparisonSuppressorTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using Microsoft.EntityFrameworkCore;

        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<User> Friends { get; set; } = new();
        }

        public class AppDbContext : DbContext
        {
            public DbSet<User> Users => Set<User>();
        }

        """;

    [Fact]
    public async Task LambdaOnDbSet_SuppressesEntityComparison_KeepsCapturedVariableCall()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public List<User> Find(AppDbContext db, string name) =>
                    db.Users.Where(u => u.Name.ToLower() == name.ToLower()).ToList();
            }
            """);

        AssertResults(
            results,
            "CA1304 kept: name.ToLower()",
            "CA1304 suppressed: u.Name.ToLower()",
            "CA1311 kept: ToLower",
            "CA1311 suppressed: ToLower",
            "CA1862 suppressed: u.Name.ToLower() == name.ToLower()");
    }

    [Fact]
    public async Task QuerySyntax_IsSuppressed()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public IQueryable<User> Find(AppDbContext db) =>
                    from u in db.Users where u.Name.ToUpper() == "ADA" select u;
            }
            """);

        AssertResults(
            results,
            "CA1304 suppressed: u.Name.ToUpper()",
            "CA1311 suppressed: ToUpper",
            "CA1862 suppressed: u.Name.ToUpper() == \"ADA\"");
    }

    [Fact]
    public async Task DelegateLambdaNestedInQuery_IsSuppressed()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public List<User> Find(AppDbContext db) =>
                    db.Users.Where(u => u.Friends.Any(f => f.Name.Contains("a"))).ToList();
            }
            """);

        AssertResults(results, "CA1307 suppressed: f.Name.Contains(\"a\")");
    }

    [Fact]
    public async Task IQueryableParameterAndProjection_AreSuppressed()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public List<string> Names(IQueryable<User> users) =>
                    users
                        .Where(u => u.Name.StartsWith("a") && string.Compare(u.Name, "m") < 0)
                        .Select(u => u.Id.ToString())
                        .ToList();
            }
            """);

        AssertResults(
            results,
            "CA1305 suppressed: u.Id.ToString()",
            "CA1307 suppressed: u.Name.StartsWith(\"a\")",
            "CA1309 suppressed: string.Compare(u.Name, \"m\")",
            "CA1310 suppressed: string.Compare(u.Name, \"m\")",
            "CA1310 suppressed: u.Name.StartsWith(\"a\")");
    }

    [Fact]
    public async Task EfCoreMethodLambda_IsSuppressed()
    {
        var results = await RunAsync(
            """
            public class Model
            {
                public void Configure(ModelBuilder modelBuilder) =>
                    modelBuilder.Entity<User>().HasQueryFilter(u => u.Name.EndsWith("x"));
            }
            """);

        AssertResults(results, "CA1307 suppressed: u.Name.EndsWith(\"x\")");
    }

    [Fact]
    public async Task EfCoreAsyncTerminalAndExecuteUpdateSetter_AreSuppressed()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public System.Threading.Tasks.Task<User?> Find(AppDbContext db) =>
                    db.Users.FirstOrDefaultAsync(u => u.Name.StartsWith("a"));

                public int Rename(AppDbContext db) =>
                    db.Users.ExecuteUpdate(s => s.SetProperty(u => u.Name, u => u.Name.ToUpper()));
            }
            """);

        AssertResults(
            results,
            "CA1304 suppressed: u.Name.ToUpper()",
            "CA1307 suppressed: u.Name.StartsWith(\"a\")",
            "CA1310 suppressed: u.Name.StartsWith(\"a\")",
            "CA1311 suppressed: ToUpper");
    }

    [Fact]
    public async Task ValueConverterLambda_KeepsWarning()
    {
        var results = await RunAsync(
            """
            public class Model
            {
                public void Configure(ModelBuilder modelBuilder) =>
                    modelBuilder.Entity<User>().Property(u => u.Name).HasConversion(v => v.ToLower(), v => v);
            }
            """);

        AssertResults(results, "CA1304 kept: v.ToLower()", "CA1311 kept: ToLower");
    }

    [Fact]
    public async Task InMemorySequences_KeepWarnings()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public List<User> Enumerable(List<User> users) =>
                    users.Where(u => u.Name.Contains("a")).ToList();

                public List<User> AsQueryable(List<User> users) =>
                    users.AsQueryable().Where(u => u.Name.Contains("b")).OrderBy(u => u.Id).Where(u => u.Name.Contains("c")).ToList();
            }
            """);

        AssertResults(
            results,
            "CA1307 kept: u.Name.Contains(\"a\")",
            "CA1307 kept: u.Name.Contains(\"b\")",
            "CA1307 kept: u.Name.Contains(\"c\")");
    }

    [Fact]
    public async Task ExpressionNotPassedToAQuery_KeepsWarning()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public Expression<Func<User, bool>> Predicate() => u => u.Name.Contains("a");
            }
            """);

        AssertResults(results, "CA1307 kept: u.Name.Contains(\"a\")");
    }

    [Fact]
    public async Task UnrelatedDiagnosticIds_AreNotSuppressed()
    {
        var results = await RunAsync(
            """
            public class Queries
            {
                public List<User> Find(AppDbContext db) =>
                    db.Users.Where(u => u.Name.Contains('a')).ToList();
            }
            """);

        AssertResults(results, "CA9999 kept: u.Name.Contains('a')");
    }

    [Fact]
    public async Task CompilationWithoutEfCore_KeepsWarnings()
    {
        var results = await RunAsync(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class User { public string Name { get; set; } = ""; }

            public class Queries
            {
                public List<User> Find(IQueryable<User> users) => users.Where(u => u.Name.Contains("a")).ToList();
            }
            """,
            includePrelude: false,
            includeEfCore: false);

        AssertResults(results, "CA1307 kept: u.Name.Contains(\"a\")");
    }

    [Fact]
    public void SupportedSuppressions_CoverTheCultureAndComparisonRules()
    {
        var suppressor = new EfQueryStringComparisonSuppressor();

        Assert.Equal(
            new[]
            {
                "LCS1304:CA1304", "LCS1305:CA1305", "LCS1307:CA1307", "LCS1309:CA1309", "LCS1310:CA1310",
                "LCS1311:CA1311", "LCS1862:CA1862"
            },
            suppressor.SupportedSuppressions.Select(s => $"{s.Id}:{s.SuppressedDiagnosticId}"));
        Assert.All(
            suppressor.SupportedSuppressions,
            s => Assert.Contains("cannot be translated", s.Justification.ToString(), StringComparison.Ordinal));
    }

    private static void AssertResults(IEnumerable<string> actual, params string[] expected)
    {
        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal),
            actual.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        string source,
        bool includePrelude = true,
        bool includeEfCore = true)
    {
        var tree = CSharpSyntaxTree.ParseText(
            includePrelude ? Prelude + source : source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            path: "Test.cs");
        var compilation = CSharpCompilation.Create(
            "SuppressorTest",
            new[] { tree },
            GetMetadataReferences(includeEfCore),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new FakeCultureAnalyzer(), new EfQueryStringComparisonSuppressor()),
                new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: (exception, _, _) => throw exception,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: true))
            .GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AD0001" or "AD0000");

        return diagnostics
            .Select(d => $"{d.Id} {(d.IsSuppressed ? "suppressed" : "kept")}: {tree.GetText().ToString(d.Location.SourceSpan)}")
            .ToList();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences(bool includeEfCore)
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator);

        foreach (var path in trustedPlatformAssemblies)
            yield return MetadataReference.CreateFromFile(path);

        if (!includeEfCore)
            yield break;

        // Copied by the test project's CopyEfCoreTestReferences target.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "efcore"), "*.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }

    /// <summary>Reports CA ids at the spans the SDK's culture and string-comparison rules use.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class FakeCultureAnalyzer : DiagnosticAnalyzer
    {
        private static readonly ImmutableDictionary<string, DiagnosticDescriptor> Descriptors =
            new[] { "CA1304", "CA1305", "CA1307", "CA1309", "CA1310", "CA1311", "CA1862", "CA9999" }
                .ToImmutableDictionary(
                    id => id,
                    id => new DiagnosticDescriptor(id, id, id, "Globalization", DiagnosticSeverity.Warning, true));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Descriptors.Values.ToImmutableArray();

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(
                AnalyzeEquality,
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression,
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
                return;

            var onString = method.ContainingType.SpecialType == SpecialType.System_String;
            var name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name : null;

            switch (method.Name)
            {
                case "ToLower" or "ToUpper" when onString && method.Parameters.Length == 0:
                    Report(context, "CA1304", invocation);
                    Report(context, "CA1311", name!);
                    break;
                case "ToString" when method.ContainingType.SpecialType == SpecialType.System_Int32 && method.Parameters.Length == 0:
                    Report(context, "CA1305", invocation);
                    break;
                case "Compare" when onString && method.Parameters.Length == 2:
                    Report(context, "CA1309", invocation);
                    Report(context, "CA1310", invocation);
                    break;
                case "Contains" or "StartsWith" or "EndsWith" when onString && method.Parameters.Length == 1:
                    if (method.Parameters[0].Type.SpecialType == SpecialType.System_Char)
                    {
                        Report(context, "CA9999", invocation);
                        break;
                    }

                    Report(context, "CA1307", invocation);
                    if (method.Name == "StartsWith")
                        Report(context, "CA1310", invocation);
                    break;
            }
        }

        private static void AnalyzeEquality(SyntaxNodeAnalysisContext context)
        {
            var binary = (BinaryExpressionSyntax)context.Node;
            if (IsCaseConversion(binary.Left) || IsCaseConversion(binary.Right))
                Report(context, "CA1862", binary);
        }

        private static bool IsCaseConversion(ExpressionSyntax expression)
        {
            return expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToLower" or "ToUpper" }
            };
        }

        private static void Report(SyntaxNodeAnalysisContext context, string id, SyntaxNode node)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors[id], node.GetLocation()));
        }
    }
}
