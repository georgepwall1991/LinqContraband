using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Tests.Architecture;

public sealed class DocumentEditorEnsureUsingTests
{
    [Fact]
    public async Task EnsureUsing_CalledTwice_InsertsNamespaceOnce()
    {
        var editor = await CreateEditorAsync("using System;\nclass C { }");

        editor.EnsureUsing("System.Linq");
        editor.EnsureUsing("System.Linq");

        var root = (CompilationUnitSyntax)editor.GetChangedRoot();
        Assert.Equal(
            new[] { "System", "System.Linq" },
            root.Usings.Select(item => item.Name!.ToString()).ToArray()
        );
    }

    [Fact]
    public async Task EnsureUsing_AfterInnerReplace_WhenFileHasNoUsings_StillInsertsUsing()
    {
        var editor = await CreateEditorAsync("class C { int M() => 1; }");
        var originalRoot = (CompilationUnitSyntax)editor.OriginalRoot;
        var literal = originalRoot.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        editor.ReplaceNode(
            literal,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(2)
            )
        );

        editor.EnsureUsing("System.Linq");

        var root = (CompilationUnitSyntax)editor.GetChangedRoot();
        Assert.Equal(
            new[] { "System.Linq" },
            root.Usings.Select(item => item.Name!.ToString()).ToArray()
        );
        Assert.Contains("2", root.ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_AfterInnerReplace_StillInsertsUsing()
    {
        var editor = await CreateEditorAsync("using System;\nclass C { int M() => 1; }");
        var originalRoot = (CompilationUnitSyntax)editor.OriginalRoot;
        var literal = originalRoot.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        editor.ReplaceNode(
            literal,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(2)
            )
        );

        editor.EnsureUsing("System.Linq");

        var root = (CompilationUnitSyntax)editor.GetChangedRoot();
        Assert.Equal(
            new[] { "System", "System.Linq" },
            root.Usings.Select(item => item.Name!.ToString()).ToArray()
        );
        Assert.Contains("2", root.ToFullString());
    }

    private static async Task<DocumentEditor> CreateEditorAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace
            .AddProject("P", LanguageNames.CSharp)
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            )
            .AddMetadataReferences(
                ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
                    .Split(Path.PathSeparator)
                    .Select(path => MetadataReference.CreateFromFile(path))
            );
        var document = project.AddDocument("C.cs", source);
        return await DocumentEditor.CreateAsync(document);
    }
}
