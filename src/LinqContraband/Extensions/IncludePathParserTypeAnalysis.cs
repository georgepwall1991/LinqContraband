using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

internal static partial class IncludePathParser
{
    private static IPropertySymbol? TryFindProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol property)
                    return property;
            }
        }

        return null;
    }

    public static bool IsCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;
        if (type.TypeKind == TypeKind.Array)
            return true;

        return TryGetCollectionElementType(type, out _);
    }

    private static bool TryGetQueryableElementType(ITypeSymbol? type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type == null)
            return false;

        if (TryGetNamedGenericElementType(type, "IQueryable", "System.Linq", out elementType))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (TryGetNamedGenericElementType(iface, "IQueryable", "System.Linq", out elementType))
                return true;
        }

        return false;
    }

    public static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (TryGetNamedGenericElementType(type, "IEnumerable", "System.Collections.Generic", out elementType))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (TryGetNamedGenericElementType(iface, "IEnumerable", "System.Collections.Generic", out elementType))
                return true;
        }

        return false;
    }

    private static bool TryGetNamedGenericElementType(
        ITypeSymbol type,
        string typeName,
        string namespaceName,
        out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.Name != typeName ||
            namedType.ContainingNamespace?.ToString() != namespaceName ||
            namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = namedType.TypeArguments[0];
        return true;
    }
}
