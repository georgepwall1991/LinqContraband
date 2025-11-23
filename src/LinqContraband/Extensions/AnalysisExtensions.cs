using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

public static class AnalysisExtensions
{
    public static bool IsFrameworkMethod(this IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (string.IsNullOrEmpty(ns)) return false;
        return ns.StartsWith("System") || ns.StartsWith("Microsoft");
    }

    public static bool IsIQueryable(this ITypeSymbol? type)
    {
        if (type == null) return false;

        if (IsIQueryableType(type)) return true;

        foreach (var i in type.AllInterfaces)
            if (IsIQueryableType(i))
                return true;
        return false;
    }

    private static bool IsIQueryableType(ITypeSymbol type)
    {
        // Check if namespace is System.Linq and Name is IQueryable
        return type.Name == "IQueryable" && type.ContainingNamespace?.ToString() == "System.Linq";
    }
}