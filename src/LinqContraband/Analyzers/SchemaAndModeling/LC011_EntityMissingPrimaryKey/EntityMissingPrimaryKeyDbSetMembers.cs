using System;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool TryGetDbSetMember(ISymbol member, out INamedTypeSymbol? entityType, out Location? location)
    {
        entityType = null;
        location = null;

        ITypeSymbol? dbSetType = null;
        switch (member)
        {
            case IPropertySymbol property:
                dbSetType = property.Type;
                location = property.Locations.FirstOrDefault();
                break;

            case IFieldSymbol field:
                if (field.IsImplicitlyDeclared || field.Name.StartsWith("<", StringComparison.Ordinal))
                    return false;

                dbSetType = field.Type;
                location = field.Locations.FirstOrDefault();
                break;
        }

        if (dbSetType is not INamedTypeSymbol namedType || !namedType.IsDbSet())
            return false;

        entityType = namedType.TypeArguments.Length > 0
            ? namedType.TypeArguments[0] as INamedTypeSymbol
            : null;

        return entityType != null && location != null;
    }
}
