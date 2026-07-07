using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private bool HasValidPrimaryKeyAttribute(INamedTypeSymbol entityType)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            if (attr.AttributeClass == null ||
                attr.AttributeClass.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore" ||
                attr.AttributeClass.Name is not ("PrimaryKeyAttribute" or "PrimaryKey"))
            {
                continue;
            }

            var propertyNames = GetPrimaryKeyPropertyNames(attr);
            if (propertyNames.Count == 0)
                return false;

            var allPropertiesValid = true;
            foreach (var propertyName in propertyNames)
            {
                if (!TryFindProperty(entityType, propertyName, out var prop) ||
                    !IsUsableKeyProperty(prop))
                {
                    allPropertiesValid = false;
                    break;
                }
            }

            if (allPropertiesValid)
                return true;
        }

        return false;
    }

    private static List<string> GetPrimaryKeyPropertyNames(AttributeData attr)
    {
        var propertyNames = new List<string>();
        foreach (var argument in attr.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var value in argument.Values)
                    if (value.Value is string propertyName)
                        propertyNames.Add(propertyName);
            }
            else if (argument.Value is string propertyName)
            {
                propertyNames.Add(propertyName);
            }
        }

        return propertyNames;
    }
}
