using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Extensions;

internal static class DocumentEditorExtensions
{
    public static void EnsureUsing(this DocumentEditor editor, string namespaceName)
    {
        if (editor.OriginalRoot is not CompilationUnitSyntax root)
            return;

        if (root.Usings.Any(u => u.Name?.ToString() == namespaceName))
            return;

        var usingDirective = editor.Generator.NamespaceImportDeclaration(namespaceName);

        if (root.Usings.Any())
        {
            editor.InsertAfter(root.Usings.Last(), usingDirective);
            return;
        }

        if (root.Members.Any())
        {
            editor.InsertBefore(root.Members.First(), usingDirective);
            return;
        }

        editor.ReplaceNode(root, root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))));
    }
}
