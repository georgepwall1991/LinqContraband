using System;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private static bool HasExecuteUpdateAsyncTokenOverload(Compilation compilation)
    {
        bool AcceptsTrailingToken(IMethodSymbol method)
        {
            if (!IsExecuteUpdateAsyncLikeMethod(method))
                return false;

            var last = method.Parameters[method.Parameters.Length - 1].Type;
            return last is { Name: "CancellationToken" } &&
                   last.ContainingNamespace?.ToString() == "System.Threading";
        }

        if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")?
                .GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(AcceptsTrailingToken) == true ||
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")?
                .GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(AcceptsTrailingToken) == true)
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(AcceptsTrailingToken);
    }

    private static bool HasExecuteUpdateAsyncSupport(Compilation compilation)
    {
        if (HasExecuteUpdateAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")) ||
            HasExecuteUpdateAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(IsExecuteUpdateAsyncLikeMethod);
    }

    private static bool HasExecuteUpdateAsyncMethod(INamedTypeSymbol? type)
    {
        return type?.GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(IsExecuteUpdateAsyncLikeMethod) == true;
    }

    private static bool IsExecuteUpdateAsyncLikeMethod(IMethodSymbol method)
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
