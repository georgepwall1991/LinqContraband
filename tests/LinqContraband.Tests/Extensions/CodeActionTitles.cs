using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Extensions;

// The code-fix verifier in Microsoft.CodeAnalysis.Testing 1.1.2 cannot check an action's title, so this
// runs the analyzer and fixer directly and returns the titles the fixer offers for each diagnostic.
internal static class CodeActionTitles
{
    public static async Task<IReadOnlyList<string>> GetAsync(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider fixer,
        string source)
    {
        using var workspace = new AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var project = workspace.AddProject("Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(references);
        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        var titles = new List<string>();
        foreach (var diagnostic in diagnostics.Where(d => fixer.FixableDiagnosticIds.Contains(d.Id)))
        {
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => titles.Add(action.Title),
                CancellationToken.None);
            await fixer.RegisterCodeFixesAsync(context);
        }

        return titles;
    }
}
