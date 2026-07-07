using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Array)
            return true;

        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var ns = named.ContainingNamespace?.ToString();
            if (ns == "System.Collections.Generic")
            {
                return named.Name is "List" or "IList" or "ICollection" or "IEnumerable"
                    or "HashSet" or "ISet" or "IReadOnlyList" or "IReadOnlyCollection";
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IEnumerable" &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic")
            {
                return true;
            }
        }

        return false;
    }
}
