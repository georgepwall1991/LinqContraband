using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodAnalyzer
{
    private static bool IsTrustedTranslatableMethod(
        IMethodSymbol method,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        if (method.IsFrameworkMethod())
            return true;
        if (HasExplicitTranslationMarker(method, dbFunctionAttribute, projectableAttribute))
            return true;

        var ns = method.ContainingNamespace?.ToString();
        if (ns == null)
            return false;

        return ns.StartsWith("Npgsql", System.StringComparison.Ordinal) ||
               ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) ||
               ns.StartsWith("NetTopologySuite", System.StringComparison.Ordinal);
    }

    private static bool HasExplicitTranslationMarker(
        IMethodSymbol method,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        if (dbFunctionAttribute == null && projectableAttribute == null)
            return false;

        foreach (var candidate in EnumerateMethodVariants(method))
        {
            foreach (var attribute in candidate.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;

                if ((dbFunctionAttribute != null && SymbolEqualityComparer.Default.Equals(attributeClass, dbFunctionAttribute)) ||
                    (projectableAttribute != null && SymbolEqualityComparer.Default.Equals(attributeClass, projectableAttribute)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateMethodVariants(IMethodSymbol method)
    {
        var pending = new Stack<IMethodSymbol>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        pending.Push(method);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            if (current.ReducedFrom != null)
                pending.Push(current.ReducedFrom);
            if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, current))
                pending.Push(current.OriginalDefinition);
            if (current.OverriddenMethod != null)
                pending.Push(current.OverriddenMethod);
        }
    }
}
