using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static bool HasExecuteUpdateSupport(Compilation compilation)
    {
        var extensionsType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        if (extensionsType != null &&
            (extensionsType.GetMembers("ExecuteUpdate").OfType<IMethodSymbol>().Any() ||
             extensionsType.GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any()))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdate", SymbolFilter.Member)
                   .OfType<IMethodSymbol>()
                   .Any(IsExecuteUpdateLikeMethod) ||
               compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
                   .OfType<IMethodSymbol>()
                   .Any(IsExecuteUpdateLikeMethod);
    }

    private static bool IsExecuteUpdateLikeMethod(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod || method.Parameters.Length == 0)
            return false;

        return method.Parameters[0].Type.IsIQueryable();
    }
}
