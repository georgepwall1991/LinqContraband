using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    internal sealed partial class PrimaryKeyCache
    {
        private readonly object syncRoot = new();
        private readonly Compilation compilation;
        private readonly bool allowFullScan;
        private readonly bool useConventionFallbackWhenConfigurationUnknown;
        private readonly ConcurrentDictionary<ITypeSymbol, ConfiguredPrimaryKey> configuredPrimaryKeys =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<SyntaxTree, byte> scannedTrees = new();
        private bool fullyScanned;

        internal PrimaryKeyCache(
            Compilation compilation,
            bool allowFullScan,
            bool useConventionFallbackWhenConfigurationUnknown)
        {
            this.compilation = compilation;
            this.allowFullScan = allowFullScan;
            this.useConventionFallbackWhenConfigurationUnknown = useConventionFallbackWhenConfigurationUnknown;
        }

        public string? TryFindSafePrimaryKey(ITypeSymbol entityType, CancellationToken cancellationToken)
        {
            var configuredKey = TryGetConfiguredPrimaryKey(entityType, cancellationToken, out var primaryKey)
                ? primaryKey
                : ConfiguredPrimaryKey.NotConfigured;

            if (configuredKey.IsConfigured)
                return configuredKey.PropertyName;

            if (!useConventionFallbackWhenConfigurationUnknown)
                return null;

            return entityType.TryFindPrimaryKey();
        }

        public void RegisterConfiguredPrimaryKey(IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name != "HasKey" ||
                !TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var entityType))
            {
                return;
            }

            configuredPrimaryKeys.TryAdd(
                entityType,
                AnalyzeKeyArgument(invocation.Arguments.FirstOrDefault()?.Value));
        }

        public void EnsureSyntaxTreeScanned(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!scannedTrees.TryAdd(syntaxTree, 0))
                return;

            var root = syntaxTree.GetRoot(cancellationToken);
            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not ("HasKey" or "HasQueryFilter"))
                {
                    continue;
                }

                if (semanticModel.GetOperation(invocationSyntax, cancellationToken) is IInvocationOperation invocation)
                {
                    RegisterConfiguredPrimaryKey(invocation);
                    RegisterQueryFilter(invocation);
                }
            }
        }

        private bool TryGetConfiguredPrimaryKey(
            ITypeSymbol entityType,
            CancellationToken cancellationToken,
            out ConfiguredPrimaryKey primaryKey)
        {
            if (configuredPrimaryKeys.TryGetValue(entityType, out primaryKey))
                return true;

            if (!allowFullScan)
                return false;

            EnsureFullyScanned(cancellationToken);
            return configuredPrimaryKeys.TryGetValue(entityType, out primaryKey);
        }

        private void EnsureFullyScanned(CancellationToken cancellationToken)
        {
            if (fullyScanned)
                return;

            lock (syncRoot)
            {
                if (fullyScanned)
                    return;

                BuildConfiguredPrimaryKeys(compilation, this, cancellationToken);
                fullyScanned = true;
            }
        }
    }
}
