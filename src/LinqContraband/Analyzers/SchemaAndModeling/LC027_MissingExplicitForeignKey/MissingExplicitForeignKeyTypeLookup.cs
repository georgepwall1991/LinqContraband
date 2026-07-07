using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private sealed partial class CompilationModel
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

    private sealed class ConfigurationScan
    {
        public HashSet<INamedTypeSymbol> OwnedEntities { get; } =
            new(SymbolEqualityComparer.Default);

        public HashSet<string> ConfiguredForeignKeys { get; } =
            new(StringComparer.Ordinal);
    }
}
