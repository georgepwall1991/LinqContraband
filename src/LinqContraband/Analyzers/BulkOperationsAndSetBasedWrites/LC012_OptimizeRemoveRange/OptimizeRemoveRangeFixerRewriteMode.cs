using System;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeFixer
{
    private static RewriteMode DetermineRewriteMode(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (!IsAsyncContext(invocation))
            return RewriteMode.Sync;

        return HasExecuteDeleteAsyncSupport(semanticModel.Compilation)
            ? RewriteMode.Async
            : RewriteMode.None;
    }

    private static bool IsAsyncContext(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AccessorDeclarationSyntax:
                    // Property/event accessors cannot be async.
                    return false;
                case BaseMethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }
        }

        return false;
    }

    private static bool HasExecuteDeleteAsyncSupport(Compilation compilation)
    {
        if (HasExecuteDeleteAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")) ||
            HasExecuteDeleteAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteDeleteAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(IsExecuteDeleteAsyncLikeMethod);
    }

    private static bool HasExecuteDeleteAsyncMethod(INamedTypeSymbol? type)
    {
        return type?.GetMembers("ExecuteDeleteAsync").OfType<IMethodSymbol>().Any(IsExecuteDeleteAsyncLikeMethod) == true;
    }

    private static bool IsExecuteDeleteAsyncLikeMethod(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod || method.Parameters.Length == 0)
            return false;

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return false;

        return method.Parameters[0].Type.IsIQueryable();
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) == true;
    }
}
