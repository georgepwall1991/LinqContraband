using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static void TryAddResolvedType(string? typeName, Compilation compilation, HashSet<INamedTypeSymbol> targetSet)
    {
        if (typeName == null) return;
        var resolved = FindTypeByName(compilation, typeName);
        if (resolved != null) targetSet.Add(resolved);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(childNs))
                yield return type;
        }
    }

    private static INamedTypeSymbol? FindTypeByName(Compilation compilation, string typeName)
    {
        var type = compilation.GetTypeByMetadataName(typeName);
        if (type != null) return type;

        var simpleName = typeName.Contains(".", StringComparison.Ordinal)
            ? typeName.Substring(typeName.LastIndexOf(".", StringComparison.Ordinal) + 1)
            : typeName;
        return FindTypeInNamespace(compilation.GlobalNamespace, simpleName, typeName);
    }

    private static INamedTypeSymbol? FindTypeInNamespace(INamespaceSymbol ns, string simpleName, string fullName)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (type.Name == simpleName)
            {
                if (fullName.Contains(".", StringComparison.Ordinal))
                {
                    var typeFullName = type.ToDisplayString();
                    if (typeFullName.Equals(fullName, StringComparison.Ordinal))
                        return type;

                    if (typeFullName.EndsWith(fullName, StringComparison.Ordinal))
                    {
                        var prefixLength = typeFullName.Length - fullName.Length;
                        if (prefixLength == 0 || typeFullName[prefixLength - 1] == '.')
                            return type;
                    }
                }
                else
                {
                    return type;
                }
            }

            foreach (var nested in type.GetTypeMembers())
            {
                if (nested.Name != simpleName)
                    continue;

                if (fullName.Contains(".", StringComparison.Ordinal))
                {
                    var nestedFullName = nested.ToDisplayString();
                    if (nestedFullName.EndsWith(fullName, StringComparison.Ordinal))
                        return nested;
                }
                else
                {
                    return nested;
                }
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            var found = FindTypeInNamespace(childNs, simpleName, fullName);
            if (found != null) return found;
        }

        return null;
    }
}
