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
        private List<INamedTypeSymbol>? allTypes;
        private ConfigurationScan? configurationScan;

        public CompilationModel(Compilation compilation)
        {
            this.compilation = compilation;
        }

        public Compilation Compilation => compilation;

        public IReadOnlyList<INamedTypeSymbol> GetAllTypes(CancellationToken cancellationToken)
        {
            if (allTypes != null)
                return allTypes;

            lock (syncRoot)
            {
                allTypes ??= BuildAllTypes(cancellationToken);
                return allTypes;
            }
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

        private List<INamedTypeSymbol> BuildAllTypes(CancellationToken cancellationToken)
        {
            var result = new List<INamedTypeSymbol>();
            AddNamespaceTypes(compilation.Assembly.GlobalNamespace, result, cancellationToken);
            return result;
        }

        private INamedTypeSymbol? FindTypeByNameCore(string typeName, CancellationToken cancellationToken)
        {
            var type = compilation.GetTypeByMetadataName(typeName);
            if (type != null)
                return type;

            var simpleName = typeName.Contains(".", StringComparison.Ordinal)
                ? typeName.Substring(typeName.LastIndexOf(".", StringComparison.Ordinal) + 1)
                : typeName;

            foreach (var candidate in GetAllTypes(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (candidate.Name != simpleName)
                    continue;

                if (!typeName.Contains(".", StringComparison.Ordinal))
                    return candidate;

                var fullName = candidate.ToDisplayString();
                if (fullName.Equals(typeName, StringComparison.Ordinal))
                    return candidate;

                if (fullName.EndsWith(typeName, StringComparison.Ordinal))
                {
                    var prefixLength = fullName.Length - typeName.Length;
                    if (prefixLength == 0 || fullName[prefixLength - 1] == '.')
                        return candidate;
                }
            }

            return null;
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
}
