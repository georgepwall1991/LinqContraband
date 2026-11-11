using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NPlusOneBenchmark;

/// <summary>
/// Compiles the embedded corpus sources and runs every LinqContraband analyzer over them, with the
/// default configuration a consumer gets from installing the package.
/// </summary>
internal static class CorpusAnalysis
{
    /// <summary>The rules whose job is to report a round trip repeated per element.</summary>
    public static readonly ImmutableHashSet<string> NPlusOneRuleIds =
        ImmutableHashSet.Create(StringComparer.Ordinal, "LC007", "LC010", "LC045");

    private const string ResourcePrefix = "Corpus/";

    /// <summary>Returns the N+1 rule ids reported inside each top-level corpus type, keyed by type name.</summary>
    public static async Task<IReadOnlyDictionary<string, string[]>> AnalyzeAsync()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
        var benchmarkAssembly = typeof(CorpusAnalysis).Assembly;

        var trees = benchmarkAssembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => ParseResource(benchmarkAssembly, name, parseOptions))
            .ToArray();

        if (trees.Length == 0)
            throw new InvalidOperationException("No embedded corpus sources were found.");

        // Everything the corpus compiled against, minus the benchmark assembly itself: the corpus
        // must be analyzed from source, and the external library from metadata.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !string.Equals(path, benchmarkAssembly.Location, StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "NPlusOneBenchmark.CorpusUnderAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "The corpus does not compile for analysis:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Take(20).Select(error => error.ToString())));
        }

        var analyzers = typeof(RuleCatalog).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() != null)
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();

        var diagnostics = await compilation
            .WithAnalyzers(analyzers, new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty))
            .GetAnalyzerDiagnosticsAsync();

        var crashes = diagnostics.Where(diagnostic => diagnostic.Id == "AD0001").ToArray();
        if (crashes.Length > 0)
        {
            throw new InvalidOperationException(
                "An analyzer threw while analyzing the corpus:" + Environment.NewLine +
                string.Join(Environment.NewLine, crashes.Select(crash => crash.ToString())));
        }

        return diagnostics
            .Where(diagnostic => NPlusOneRuleIds.Contains(diagnostic.Id) && diagnostic.Location.IsInSource)
            .GroupBy(OwningTypeName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(diagnostic => diagnostic.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static SyntaxTree ParseResource(Assembly assembly, string resourceName, CSharpParseOptions parseOptions)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return CSharpSyntaxTree.ParseText(SourceText.From(stream), parseOptions, path: resourceName);
    }

    /// <summary>The outermost type declaration containing the diagnostic: a case class, or a shared layer type.</summary>
    private static string OwningTypeName(Diagnostic diagnostic)
    {
        var node = diagnostic.Location.SourceTree!.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        var outermost = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().LastOrDefault();
        return outermost?.Identifier.ValueText ?? diagnostic.Location.SourceTree.FilePath;
    }
}
