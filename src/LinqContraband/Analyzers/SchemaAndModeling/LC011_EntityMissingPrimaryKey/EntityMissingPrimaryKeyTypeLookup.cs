using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private sealed class CompilationModel
    {
        private readonly object syncRoot = new();
        private readonly Compilation compilation;
        private readonly Dictionary<string, INamedTypeSymbol?> typeLookupCache = new(StringComparer.Ordinal);
        private List<INamedTypeSymbol>? allTypes;
        private EntityTypeConfigurationScan? entityTypeConfigurationScan;

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

        public EntityTypeConfigurationScan GetEntityTypeConfigurationScan(CancellationToken cancellationToken)
        {
            if (entityTypeConfigurationScan != null)
                return entityTypeConfigurationScan;

            lock (syncRoot)
            {
                entityTypeConfigurationScan ??= BuildEntityTypeConfigurationScan(this, cancellationToken);
                return entityTypeConfigurationScan;
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

    private sealed class EntityTypeConfigurationScan
    {
        public HashSet<INamedTypeSymbol> ConfiguredEntities { get; } =
            new(SymbolEqualityComparer.Default);

        public HashSet<INamedTypeSymbol> KeylessEntities { get; } =
            new(SymbolEqualityComparer.Default);
    }

    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "Entity")
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                    return typeArg.ToString();
            }

            current = current switch
            {
                InvocationExpressionSyntax inv => inv.Expression,
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null
            };
        }

        return null;
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
