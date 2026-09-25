using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Extensions;

internal static class DocumentEditorExtensions
{
    /// <summary>
    /// Adds <c>using <paramref name="namespaceName"/>;</c> unless a file, namespace or global using already
    /// imports it. The new using goes below a leading file header, and at its sorted place when the file's
    /// usings are already sorted.
    /// </summary>
    public static void EnsureUsing(this DocumentEditor editor, string namespaceName)
    {
        if (editor.OriginalRoot is not CompilationUnitSyntax original)
            return;

        if (IsImportedByNamespaceOrGlobalUsing(editor, original, namespaceName))
            return;

        editor.ReplaceNode(
            original,
            (current, _) =>
            {
                if (current is not CompilationUnitSyntax compilationUnit)
                    return current;

                if (compilationUnit.Usings.Any(item => Imports(item, namespaceName)))
                    return compilationUnit;

                return AddUsing(compilationUnit, namespaceName);
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

    private static bool IsImportedByNamespaceOrGlobalUsing(DocumentEditor editor, CompilationUnitSyntax root, string namespaceName)
    {
        // Namespace-scoped usings count when every top-level declaration in the file sits in a namespace that
        // imports the namespace, as in `namespace App { using Microsoft.EntityFrameworkCore; ... }`.
        var topLevelMembers = root
            .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>()
            .Where(member => member is not BaseNamespaceDeclarationSyntax)
            .ToList();
        if (topLevelMembers.Count > 0 &&
            topLevelMembers.All(member => member.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Any(declaration => declaration.Usings.Any(item => Imports(item, namespaceName)))))
            return true;

        // A global using in any file of the project, including the SDK's generated implicit usings.
        foreach (var tree in editor.SemanticModel.Compilation.SyntaxTrees)
        {
            if (tree.GetRoot() is CompilationUnitSyntax unit &&
                unit.Usings.Any(item => !item.GlobalKeyword.IsKind(SyntaxKind.None) && Imports(item, namespaceName)))
                return true;
        }

        return false;
    }

    private static bool Imports(UsingDirectiveSyntax item, string namespaceName)
    {
        return item.Alias == null &&
               item.StaticKeyword.IsKind(SyntaxKind.None) &&
               NormalizeName(item.Name?.ToString()) == namespaceName;
    }

    private static string? NormalizeName(string? name)
    {
        if (name == null)
            return null;

        name = name.Replace(" ", string.Empty);
        return name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring("global::".Length) : name;
    }

    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
        var endOfLine = DetectEndOfLine(compilationUnit);

        if (compilationUnit.Usings.Count == 0)
        {
            if (compilationUnit.Externs.Count == 0 && TryTakeFileHeader(compilationUnit, out var header, out var withoutHeader))
            {
                // Keep a leading copyright or file comment above the new using.
                return withoutHeader.WithUsings(SyntaxFactory.SingletonList(
                    directive.NormalizeWhitespace().WithLeadingTrivia(header).WithTrailingTrivia(endOfLine, endOfLine)));
            }

            return compilationUnit.AddUsings(directive);
        }

        var usings = compilationUnit.Usings;
        var plain = Enumerable.Range(0, usings.Count)
            .TakeWhile(index => IsPlainUsing(usings[index]) || !usings[index].GlobalKeyword.IsKind(SyntaxKind.None))
            .Where(index => IsPlainUsing(usings[index]))
            .ToList();
        var names = plain.Select(index => NormalizeName(usings[index].Name?.ToString()) ?? string.Empty).ToList();
        var comparer = PickSortOrder(names);
        if (comparer == null || plain.Count == 0)
            return compilationUnit.AddUsings(directive);

        var position = plain[plain.Count - 1] + 1;
        foreach (var index in plain)
        {
            if (comparer.Compare(NormalizeName(usings[index].Name?.ToString()) ?? string.Empty, namespaceName) > 0)
            {
                position = index;
                break;
            }
        }

        directive = directive.NormalizeWhitespace().WithTrailingTrivia(endOfLine);
        if (position == 0)
        {
            // The first using carries the file header; the new first using takes it over.
            var first = usings[0];
            directive = directive.WithLeadingTrivia(first.GetLeadingTrivia());
            usings = usings.Replace(first, first.WithLeadingTrivia());
        }

        return compilationUnit.WithUsings(usings.Insert(position, directive));
    }

    private static bool IsPlainUsing(UsingDirectiveSyntax item)
    {
        return item.GlobalKeyword.IsKind(SyntaxKind.None) && item.Alias == null && item.StaticKeyword.IsKind(SyntaxKind.None);
    }

    // The sort order the existing usings already follow: System namespaces first (the .NET default), or plain
    // ordinal. Null when they follow neither, so the new using is appended instead of guessing.
    private static IComparer<string>? PickSortOrder(IReadOnlyList<string> names)
    {
        foreach (var comparer in new IComparer<string>[] { SystemFirstComparer.Instance, StringComparer.OrdinalIgnoreCase })
        {
            var sorted = true;
            for (var i = 1; i < names.Count && sorted; i++)
                sorted = comparer.Compare(names[i - 1], names[i]) <= 0;

            if (sorted)
                return comparer;
        }

        return null;
    }

    private static bool TryTakeFileHeader(CompilationUnitSyntax compilationUnit, out SyntaxTriviaList header, out CompilationUnitSyntax withoutHeader)
    {
        header = default;
        withoutHeader = compilationUnit;

        var firstToken = compilationUnit.GetFirstToken(includeZeroWidth: true);
        var leading = firstToken.LeadingTrivia;
        if (!leading.Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                   trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) ||
            leading.Any(trivia => trivia.IsDirective))
            return false;

        header = leading;
        withoutHeader = compilationUnit.ReplaceToken(firstToken, firstToken.WithLeadingTrivia());
        return true;
    }

    private static SyntaxTrivia DetectEndOfLine(SyntaxNode root)
    {
        var existing = root.DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        return existing.IsKind(SyntaxKind.EndOfLineTrivia) ? existing : SyntaxFactory.EndOfLine("\n");
    }

    private sealed class SystemFirstComparer : IComparer<string>
    {
        public static readonly SystemFirstComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var xSystem = IsSystem(x);
            var ySystem = IsSystem(y);
            if (xSystem != ySystem)
                return xSystem ? -1 : 1;

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }

        private static bool IsSystem(string? name)
        {
            return name == "System" || name?.StartsWith("System.", StringComparison.Ordinal) == true;
        }
    }
}
