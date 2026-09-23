using System;
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

    /// <summary>
    /// Adds <c>using <paramref name="namespaceName"/>;</c> when the rewrite introduces a call to the
    /// extension method <paramref name="methodName"/> on <paramref name="receiverType"/> that the code at
    /// <paramref name="position"/> cannot bind yet. A using already in scope (file, namespace or global)
    /// that exposes a matching extension leaves the file untouched.
    /// </summary>
    /// <param name="accept">
    /// Optional filter on the unreduced extension method, for example to require an
    /// <c>IQueryable</c> receiver so an <c>IAsyncEnumerable</c> overload does not count.
    /// </param>
    public static void EnsureUsingForExtensionMethod(
        this DocumentEditor editor,
        SemanticModel semanticModel,
        int position,
        ITypeSymbol? receiverType,
        string methodName,
        string? namespaceName,
        Func<IMethodSymbol, bool>? accept = null)
    {
        if (namespaceName is not { Length: > 0 })
            return;

        if (receiverType != null && receiverType.TypeKind != TypeKind.Error)
        {
            var inScope = semanticModel
                .LookupSymbols(position, receiverType, methodName, includeReducedExtensionMethods: true)
                .OfType<IMethodSymbol>()
                .Any(method => method.ReducedFrom is { } extension && (accept == null || accept(extension)));
            if (inScope)
                return;
        }

        editor.EnsureUsing(namespaceName);
    }
}
