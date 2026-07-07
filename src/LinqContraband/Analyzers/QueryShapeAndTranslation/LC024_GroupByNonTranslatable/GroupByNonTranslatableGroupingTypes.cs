using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC024_GroupByNonTranslatable;

public sealed partial class GroupByNonTranslatableAnalyzer
{
    private static bool IsGroupingQueryable(ITypeSymbol? type)
    {
        if (type == null) return false;

        var elementType = GetQueryableElementType(type);
        if (elementType == null) return false;

        return IsGrouping(elementType);
    }

    private static ITypeSymbol? GetQueryableElementType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.Name == "IQueryable" && named.ContainingNamespace?.ToString() == "System.Linq")
        {
            return named.TypeArguments.Length > 0 ? named.TypeArguments[0] : null;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IQueryable" &&
                iface.ContainingNamespace?.ToString() == "System.Linq" &&
                iface.TypeArguments.Length > 0)
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsGrouping(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            if (named.Name == "IGrouping" && named.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IGrouping" &&
                iface.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        }

        return false;
    }
}
