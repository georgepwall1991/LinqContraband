using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool AnyUsingDirectiveInScope(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        Func<UsingDirectiveSyntax, bool> predicate,
        bool includeCurrentSyntaxTreeInGlobalSearch)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax currentCompilationUnit &&
            currentCompilationUnit.Usings.Any(predicate))
        {
            return true;
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (namespaceDeclaration.Usings.Any(predicate))
                return true;
        }

        foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileScopedNamespace.Usings.Any(predicate))
                return true;
        }

        foreach (var syntaxTree in compilationModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!includeCurrentSyntaxTreeInGlobalSearch && syntaxTree == node.SyntaxTree)
                continue;

            if (syntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax compilationUnit)
                continue;

            if (compilationUnit.Usings.Any(usingDirective =>
                    usingDirective.GlobalKeyword.RawKind != 0 &&
                    predicate(usingDirective)))
            {
                return true;
            }
        }

        return false;
    }
}
