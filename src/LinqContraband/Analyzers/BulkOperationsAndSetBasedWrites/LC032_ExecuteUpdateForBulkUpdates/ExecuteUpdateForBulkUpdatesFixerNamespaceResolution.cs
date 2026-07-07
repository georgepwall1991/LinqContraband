using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private static string? ResolveExecuteUpdateNamespace(Compilation compilation, bool async)
    {
        var methodName = async ? "ExecuteUpdateAsync" : "ExecuteUpdate";

        foreach (var typeName in new[]
                 {
                     "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
                     "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                 })
        {
            var method = compilation.GetTypeByMetadataName(typeName)?
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(IsExecuteUpdateLikeMethod);

            if (method is not null)
                return method.ContainingNamespace?.ToString();
        }

        return compilation.GetSymbolsWithName(methodName, SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsExecuteUpdateLikeMethod)?
            .ContainingNamespace?.ToString();
    }

    private static bool IsExecuteUpdateLikeMethod(IMethodSymbol method) =>
        method.IsExtensionMethod && method.Parameters.Length > 0 && method.Parameters[0].Type.IsIQueryable();
}
