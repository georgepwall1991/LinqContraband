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

    [Fact]
    public async Task EnsureUsing_GlobalUsingInAnotherFile_AddsNothing()
    {
        var editor = await CreateEditorAsync("using System;\nclass C { }", "global using System.Linq;\n");

        editor.EnsureUsing("System.Linq");

        Assert.Equal("using System;\nclass C { }", editor.GetChangedRoot().ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_NamespaceScopedUsingCoversTheFile_AddsNothing()
    {
        const string source = "namespace App\n{\n    using System.Linq;\n\n    class C { }\n}\n";
        var editor = await CreateEditorAsync(source);

        editor.EnsureUsing("System.Linq");

        Assert.Equal(source, editor.GetChangedRoot().ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_NamespaceScopedUsingInAnotherNamespace_StillAddsTheUsing()
    {
        var editor = await CreateEditorAsync("namespace Lib\n{\n    using System.Linq;\n    class L { }\n}\nnamespace App\n{\n    class C { }\n}\n");

        editor.EnsureUsing("System.Linq");

        var root = (CompilationUnitSyntax)editor.GetChangedRoot();
        Assert.Equal(new[] { "System.Linq" }, root.Usings.Select(item => item.Name!.ToString()).ToArray());
    }

    [Fact]
    public async Task EnsureUsing_FileHeaderWithoutUsings_PutsTheUsingBelowTheHeader()
    {
        var editor = await CreateEditorAsync("// Copyright (c) Contoso.\n\nnamespace App;\n\nclass C { }\n");

        editor.EnsureUsing("System.Linq");

        Assert.Equal(
            "// Copyright (c) Contoso.\n\nusing System.Linq;\n\nnamespace App;\n\nclass C { }\n",
            editor.GetChangedRoot().ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_SortedUsings_InsertsAtTheSortedPlace()
    {
        var editor = await CreateEditorAsync("using System;\nusing System.Collections.Generic;\nusing Microsoft.Win32;\n\nclass C { }\n");

        editor.EnsureUsing("System.Linq");

        Assert.Equal(
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing Microsoft.Win32;\n\nclass C { }\n",
            editor.GetChangedRoot().ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_SortedUsingsBelowAHeader_KeepsTheHeaderOnTop()
    {
        var editor = await CreateEditorAsync("// Header\nusing System.Text;\n\nclass C { }\n");

        editor.EnsureUsing("System.Linq");

        Assert.Equal("// Header\nusing System.Linq;\nusing System.Text;\n\nclass C { }\n", editor.GetChangedRoot().ToFullString());
    }

    [Fact]
    public async Task EnsureUsing_UnsortedUsings_AppendsTheUsing()
    {
        var editor = await CreateEditorAsync("using System.Text;\nusing System;\nclass C { }");

        editor.EnsureUsing("System.Linq");

        var root = (CompilationUnitSyntax)editor.GetChangedRoot();
        Assert.Equal(
            new[] { "System.Text", "System", "System.Linq" },
            root.Usings.Select(item => item.Name!.ToString()).ToArray());
    }

    private static async Task<DocumentEditor> CreateEditorAsync(string source, string? otherFile = null)
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
        if (otherFile != null)
            project = project.AddDocument("GlobalUsings.cs", otherFile).Project;

        var document = project.AddDocument("C.cs", source);
        return await DocumentEditor.CreateAsync(document);
    }
}
