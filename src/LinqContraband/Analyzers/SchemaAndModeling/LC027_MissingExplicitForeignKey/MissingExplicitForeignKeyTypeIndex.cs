using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private sealed partial class CompilationModel
    {
        private TypeIndex BuildTypeIndex(CancellationToken cancellationToken)
        {
            var result = new List<INamedTypeSymbol>();
            AddNamespaceTypes(compilation.Assembly.GlobalNamespace, result, cancellationToken);
            var lookup = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

            foreach (var type in result)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddLookupName(lookup, type.Name, type);

                var fullName = type.ToDisplayString();
                AddLookupName(lookup, fullName, type);

                for (var index = fullName.IndexOf(".", StringComparison.Ordinal);
                     index >= 0;
                     index = fullName.IndexOf(".", index + 1, StringComparison.Ordinal))
                {
                    if (index + 1 < fullName.Length)
                        AddLookupName(lookup, fullName.Substring(index + 1), type);
                }
            }

            return new TypeIndex(result, lookup);
        }
    }

    private sealed class TypeIndex
    {
        private readonly Dictionary<string, INamedTypeSymbol> lookup;

        public TypeIndex(List<INamedTypeSymbol> allTypes, Dictionary<string, INamedTypeSymbol> lookup)
        {
            AllTypes = allTypes;
            this.lookup = lookup;
        }

        public List<INamedTypeSymbol> AllTypes { get; }

        public bool TryFind(string typeName, out INamedTypeSymbol type)
        {
            return lookup.TryGetValue(typeName, out type!);
        }
    }

    private static void AddNamespaceTypes(
        INamespaceSymbol ns,
        List<INamedTypeSymbol> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var type in ns.GetTypeMembers())
        {
            AddTypeAndNestedTypes(type, result, cancellationToken);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            AddNamespaceTypes(childNs, result, cancellationToken);
        }
    }

    private static void AddTypeAndNestedTypes(
        INamedTypeSymbol type,
        List<INamedTypeSymbol> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        result.Add(type);

        foreach (var nested in type.GetTypeMembers())
        {
            AddTypeAndNestedTypes(nested, result, cancellationToken);
        }
    }

    private static void AddLookupName(
        Dictionary<string, INamedTypeSymbol> lookup,
        string name,
        INamedTypeSymbol type)
    {
        if (name.Length > 0 && !lookup.ContainsKey(name))
            lookup.Add(name, type);
    }
}
