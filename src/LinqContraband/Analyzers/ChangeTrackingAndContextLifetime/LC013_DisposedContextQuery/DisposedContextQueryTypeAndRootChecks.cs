using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

public sealed partial class DisposedContextQueryAnalyzer
{
    private bool IsDeferredType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.IsIQueryable() ||
               ImplementsInterface(type, "System.Collections.Generic.IAsyncEnumerable`1");
    }

    private static bool ImplementsInterface(ITypeSymbol type, string interfaceMetadataName)
    {
        if (GetFullMetadataName(type) == interfaceMetadataName)
            return true;

        foreach (var i in type.AllInterfaces)
            if (GetFullMetadataName(i) == interfaceMetadataName)
                return true;
        return false;
    }

    private static string GetFullMetadataName(ITypeSymbol type)
    {
        return $"{type.ContainingNamespace}.{type.MetadataName}";
    }

    private static bool IsSupportedExecutableRoot(IOperation? executableRoot)
    {
        return executableRoot is IMethodBodyOperation or ILocalFunctionOperation or IAnonymousFunctionOperation;
    }
}
