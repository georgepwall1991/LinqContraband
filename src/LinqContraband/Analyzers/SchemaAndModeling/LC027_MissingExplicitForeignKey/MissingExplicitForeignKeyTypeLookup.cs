using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private sealed class CompilationModel
    {
        private readonly object syncRoot = new();
        private readonly Compilation compilation;
        private readonly Dictionary<string, INamedTypeSymbol?> typeLookupCache = new(StringComparer.Ordinal);
        private TypeIndex? typeIndex;
        private ConfigurationScan? configurationScan;

        public CompilationModel(Compilation compilation)
        {
            this.compilation = compilation;
        }

        public Compilation Compilation => compilation;

        public IReadOnlyList<INamedTypeSymbol> GetAllTypes(CancellationToken cancellationToken)
        {
            return GetTypeIndex(cancellationToken).AllTypes;
        }

        public INamedTypeSymbol? FindTypeByName(string typeName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (syncRoot)
            {
                if (!typeLookupCache.TryGetValue(typeName, out var cachedType))
                {
                    cachedType = FindTypeByNameCore(typeName, cancellationToken);
                    typeLookupCache[typeName] = cachedType;
                }

                return cachedType;
            }
        }

        public ConfigurationScan GetConfigurationScan(CancellationToken cancellationToken)
        {
            if (configurationScan != null)
                return configurationScan;

            lock (syncRoot)
            {
                configurationScan ??= BuildConfigurationScan(this, cancellationToken);
                return configurationScan;
            }
        }

        private TypeIndex GetTypeIndex(CancellationToken cancellationToken)
        {
            if (typeIndex != null)
                return typeIndex;

            lock (syncRoot)
            {
                typeIndex ??= BuildTypeIndex(cancellationToken);
                return typeIndex;
            }
        }

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

        private INamedTypeSymbol? FindTypeByNameCore(string typeName, CancellationToken cancellationToken)
        {
            var type = compilation.GetTypeByMetadataName(typeName);
            if (type != null)
                return type;

            return GetTypeIndex(cancellationToken).TryFind(typeName, out var indexedType)
                ? indexedType
                : null;
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

    private sealed class ConfigurationScan
    {
        public HashSet<INamedTypeSymbol> OwnedEntities { get; } =
            new(SymbolEqualityComparer.Default);

        public HashSet<string> ConfiguredForeignKeys { get; } =
            new(StringComparer.Ordinal);
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
