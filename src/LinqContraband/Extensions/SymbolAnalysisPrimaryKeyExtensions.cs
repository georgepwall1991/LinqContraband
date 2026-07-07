using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static string? TryFindPrimaryKey(this ITypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass == null) continue;
                    if (IsDataAnnotationsKeyAttribute(attr.AttributeClass))
                    {
                        return prop.Name;
                    }
                }

                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;
                if (prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;
            }

            current = current.BaseType;
        }

        return null;
    }

    private static bool IsDataAnnotationsKeyAttribute(INamedTypeSymbol attributeClass)
    {
        return attributeClass.Name == "KeyAttribute" &&
               attributeClass.ContainingNamespace?.ToString() == "System.ComponentModel.DataAnnotations";
    }
}
