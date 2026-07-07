using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByFixer
{
    private static bool HasCompositeKeyAttribute(ITypeSymbol entityType)
    {
        var propertyKeyCount = 0;
        for (var current = entityType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            // EF Core 7+ class-level composite-key declaration:
            //   [PrimaryKey(nameof(TenantId), nameof(Id))]
            // Treated as a composite key when two or more property names are
            // supplied, regardless of which key part the entity also exposes
            // through `[Key]` or the `Id` / `<Entity>Id` conventions.
            foreach (var attr in current.GetAttributes())
            {
                if (attr.AttributeClass is { Name: "PrimaryKeyAttribute" } primaryKeyAttr &&
                    primaryKeyAttr.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore" &&
                    CountPrimaryKeyParts(attr) >= 2)
                {
                    return true;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass is { Name: "KeyAttribute" } attrClass &&
                        attrClass.ContainingNamespace?.ToString() == "System.ComponentModel.DataAnnotations")
                    {
                        propertyKeyCount++;
                        if (propertyKeyCount >= 2) return true;
                        break;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasKeylessAttribute(ITypeSymbol entityType)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            if (attr.AttributeClass is { Name: "KeylessAttribute" } attrClass &&
                attrClass.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }
        }

        return false;
    }

    private static int CountPrimaryKeyParts(AttributeData attribute)
    {
        // [PrimaryKey] accepts either a params string[] of property names or
        // a single property name plus additional names. Count both positional
        // and array-valued arguments.
        var count = 0;
        foreach (var arg in attribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array)
                count += arg.Values.Length;
            else
                count += 1;
        }

        return count;
    }
}
