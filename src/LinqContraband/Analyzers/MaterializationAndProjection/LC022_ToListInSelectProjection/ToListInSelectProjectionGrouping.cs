using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC022_ToListInSelectProjection;

public sealed partial class ToListInSelectProjectionAnalyzer
{
    private static bool IsGroupingQueryable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var elementType = GetQueryableElementType(type);
        return elementType != null && IsGroupingType(elementType);
    }

    private static ITypeSymbol? GetQueryableElementType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.Name == "IQueryable" &&
            named.ContainingNamespace?.ToString() == "System.Linq")
        {
            return named.TypeArguments.Length > 0 ? named.TypeArguments[0] : null;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.Name == "IQueryable" &&
                iface.ContainingNamespace?.ToString() == "System.Linq" &&
                iface.TypeArguments.Length > 0)
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsGroupingType(ITypeSymbol? type)
    {
        if (type == null) return false;

        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.Name == "IGrouping" && named.ContainingNamespace?.ToString() == "System.Linq")
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IGrouping" &&
                iface.ContainingNamespace?.ToString() == "System.Linq")
            {
                return true;
            }
        }

        return false;
    }
}
