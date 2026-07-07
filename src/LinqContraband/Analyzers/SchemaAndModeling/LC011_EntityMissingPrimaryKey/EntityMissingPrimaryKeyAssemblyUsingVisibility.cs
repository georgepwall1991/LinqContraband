using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool HasAssemblyAliasInScope(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        return AnyUsingDirectiveInScope(
            node,
            compilationModel,
            cancellationToken,
            IsNonSystemReflectionAssemblyAlias,
            includeCurrentSyntaxTreeInGlobalSearch: true);
    }

    private static bool HasSystemReflectionAssemblyAliasInScope(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        return AnyUsingDirectiveInScope(
            node,
            compilationModel,
            cancellationToken,
            IsSystemReflectionAssemblyAlias,
            includeCurrentSyntaxTreeInGlobalSearch: true);
    }

    private static bool HasVisibleAssemblyType(
        INamedTypeSymbol dbContextType,
        INamedTypeSymbol? systemReflectionAssemblyType)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.ContainingType)
        {
            if (currentType.GetTypeMembers("Assembly").Any(type => !IsSystemReflectionAssemblyType(type, systemReflectionAssemblyType)))
                return true;
        }

        for (var currentNamespace = dbContextType.ContainingNamespace;
             currentNamespace != null;
             currentNamespace = currentNamespace.ContainingNamespace)
        {
            if (currentNamespace.GetTypeMembers("Assembly").Any(type => !IsSystemReflectionAssemblyType(type, systemReflectionAssemblyType)))
                return true;

            if (currentNamespace.IsGlobalNamespace)
                break;
        }

        return false;
    }

    private static bool HasSystemReflectionUsing(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        return AnyUsingDirectiveInScope(
            node,
            compilationModel,
            cancellationToken,
            IsSystemReflectionUsing,
            includeCurrentSyntaxTreeInGlobalSearch: false);
    }

    private static bool IsSystemReflectionUsing(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Name?.ToString() == "System.Reflection";
    }

    private static bool IsNonSystemReflectionAssemblyAlias(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias?.Name.Identifier.ValueText == "Assembly" &&
               !IsSystemReflectionAssemblyAlias(usingDirective);
    }

    private static bool IsSystemReflectionAssemblyAlias(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias?.Name.Identifier.ValueText == "Assembly" &&
               usingDirective.Name?.ToString() is "System.Reflection.Assembly" or "global::System.Reflection.Assembly";
    }

    private static bool IsSystemReflectionAssemblyType(
        INamedTypeSymbol type,
        INamedTypeSymbol? systemReflectionAssemblyType)
    {
        return systemReflectionAssemblyType != null &&
               SymbolEqualityComparer.Default.Equals(type, systemReflectionAssemblyType);
    }

}
