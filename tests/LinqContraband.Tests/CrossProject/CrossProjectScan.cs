using System.Collections.Immutable;
using LinqContraband.CrossProject;
using LinqContraband.Scan;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace LinqContraband.Tests.CrossProject;

/// <summary>One project of an in-memory solution. It references every project listed before it.</summary>
internal sealed record ScanProject(string Name, string Source)
{
    /// <summary>Adds a reference to xUnit, which is what marks a project as a test project.</summary>
    public bool IsTestProject { get; init; }

    /// <summary>Where the project's assembly is written for the projects after it, instead of an in-memory image.</summary>
    public string? ReferencePath { get; init; }

    public ImmutableDictionary<string, ReportDiagnostic> NoWarn { get; init; } = ImmutableDictionary<string, ReportDiagnostic>.Empty;

    /// <summary>Severities an <c>.editorconfig</c> would set for this project's files.</summary>
    public ImmutableDictionary<string, ReportDiagnostic> EditorConfig { get; init; } = ImmutableDictionary<string, ReportDiagnostic>.Empty;
}

internal sealed record ScanFinding(string Rule, string Code, string Severity, string Message);

/// <summary>
/// The scanner's cross-project pass without the build: compiles each project against real EF Core, hands the next
/// project a metadata-only image (what a build's project reference is), runs the fact exporter the way a scan
/// build does, and joins the facts with <see cref="CrossProjectJoin"/>.
/// </summary>
internal static class CrossProjectScan
{
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        """;

    /// <summary>The findings the join raises, each with the trimmed source line it points at.</summary>
    public static async Task<IReadOnlyList<ScanFinding>> FindAsync(params ScanProject[] projects)
    {
        var sources = projects.ToDictionary(project => project.Name + ".cs", project => (Usings + project.Source).Split('\n'));
        var facts = await ExportAsync(exportFacts: true, projects);
        return CrossProjectJoin.Resolve(facts)
            .OrderBy(finding => finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .Select(finding => new ScanFinding(finding.RuleId, sources[finding.Path][finding.Line - 1].Trim(), finding.Severity, finding.Message))
            .ToList();
    }

    /// <summary>Every fact the exporter writes for the projects, in the form the scanner reads from a compiler log.</summary>
    public static async Task<IReadOnlyList<CrossProjectFact>> ExportAsync(bool exportFacts, params ScanProject[] projects)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = FrameworkReferences().ToList();
        var facts = new List<CrossProjectFact>();

        foreach (var project in projects)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable)
                .WithSpecificDiagnosticOptions(project.NoWarn)
                .WithSyntaxTreeOptionsProvider(new TreeOptions(project.EditorConfig));
            var projectReferences = project.IsTestProject
                ? references.Append(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location))
                : references;
            var compilation = CSharpCompilation.Create(
                project.Name,
                [CSharpSyntaxTree.ParseText(Usings + project.Source, parseOptions, path: project.Name + ".cs")],
                projectReferences,
                options);

            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, project.Name + " does not compile:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

            var diagnostics = await compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new CrossProjectFactExporter()),
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new ScanBuildOptions(exportFacts)))
                .GetAnalyzerDiagnosticsAsync();

            foreach (var diagnostic in diagnostics)
            {
                Assert.Equal(CrossProjectJoin.FactRuleId, diagnostic.Id);
                var position = diagnostic.Location.GetLineSpan();
                Assert.True(
                    CrossProjectJoin.TryParse(
                        diagnostic.GetMessage(),
                        position.Path,
                        position.StartLinePosition.Line + 1,
                        position.StartLinePosition.Character + 1,
                        out var fact),
                    "Not a fact: " + diagnostic.GetMessage());
                facts.Add(fact);
            }

            references.Add(ToReference(compilation, project.ReferencePath));
        }

        return facts;
    }

    private static MetadataReference ToReference(CSharpCompilation compilation, string? path)
    {
        if (path is null)
        {
            // A reference assembly: signatures without bodies, which is what a project reference compiles against.
            using var image = new MemoryStream();
            Assert.True(compilation.Emit(image, options: new EmitOptions(metadataOnly: true)).Success);
            return MetadataReference.CreateFromImage(image.ToArray());
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
            Assert.True(compilation.Emit(stream).Success);
        return MetadataReference.CreateFromFile(path);
    }

    /// <summary>
    /// The framework and EF Core, without the test host's own assemblies: a reference to xUnit is what marks a
    /// project as a test project.
    /// </summary>
    private static IEnumerable<MetadataReference> FrameworkReferences()
    {
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "mscorlib.dll" or "netstandard.dll")
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }

        // Copied by the test project's CopyEfCoreTestReferences target.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "efcore"), "*.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }

    private sealed class ScanBuildOptions(bool exportFacts) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(exportFacts);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values(false);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(false);
    }

    private sealed class Values(bool exportFacts) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = "true";
            return exportFacts && key == ScanTargets.CrossProjectFactsOption;
        }
    }

    private sealed class TreeOptions(ImmutableDictionary<string, ReportDiagnostic> severities) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.NotGenerated;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity) =>
            severities.TryGetValue(diagnosticId, out severity);

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
    }
}
