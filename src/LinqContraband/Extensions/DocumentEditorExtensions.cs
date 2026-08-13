using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Extensions;

internal static class DocumentEditorExtensions
{
    public static void EnsureUsing(this DocumentEditor editor, string namespaceName)
    {
        if (editor.OriginalRoot is not CompilationUnitSyntax original)
            return;

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
        editor.ReplaceNode(
            original,
            (current, _) =>
            {
                if (current is not CompilationUnitSyntax compilationUnit)
                    return current;

                if (compilationUnit.Usings.Any(item => item.Name?.ToString() == namespaceName))
                    return compilationUnit;

                return compilationUnit.AddUsings(usingDirective);
            }
        );
    }
}
