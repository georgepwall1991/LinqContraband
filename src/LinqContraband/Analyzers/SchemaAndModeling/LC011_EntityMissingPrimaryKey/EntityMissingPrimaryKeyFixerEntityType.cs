using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyFixer
{
    private static bool TryGetEntityType(IPropertySymbol? propertySymbol, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (propertySymbol?.Type is not INamedTypeSymbol dbSetType || dbSetType.TypeArguments.Length == 0)
            return false;

        entityType = dbSetType.TypeArguments[0];
        return true;
    }

    private static bool HasIdMember(ITypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            if (current.GetMembers().Any(member => member.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)))
                return true;

            current = current.BaseType;
        }

        return false;
    }
}
