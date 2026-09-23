using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly ConcurrentDictionary<ITypeSymbol, ConfiguredPrimaryKey> configuredPrimaryKeys =
            new(SymbolEqualityComparer.Default);

        // Guarded by syncRoot. A tree is recorded only once its scan completed, so a scan
        // cancelled halfway is redone rather than trusted.
        private readonly HashSet<SyntaxTree> scannedTrees = new();
        private volatile bool fullyScanned;

        internal PrimaryKeyCache(Compilation compilation)
        {
            this.compilation = compilation;
        }

        /// <summary>
        /// The property Find would look up for <paramref name="entityType"/>, or null when it
        /// cannot be proven. Fluent configuration anywhere in the compilation wins, then the
        /// EF Core [Keyless]/[PrimaryKey] attributes, then [Key] and the Id/{Type}Id convention.
        /// </summary>
        public string? TryFindSafePrimaryKey(ITypeSymbol entityType, CancellationToken cancellationToken)
        {
            EnsureFullyScanned(cancellationToken);

            if (configuredPrimaryKeys.TryGetValue(entityType, out var configuredKey))
                return configuredKey.PropertyName;

            var attributeKey = AnalyzeKeyAttributes(entityType);
            if (attributeKey.IsConfigured)
                return attributeKey.PropertyName;

            return entityType.TryFindPrimaryKey();
        }

        public void RegisterConfiguredPrimaryKey(IInvocationOperation invocation)
        {
            var methodName = invocation.TargetMethod.Name;
            if (methodName is not ("HasKey" or "HasNoKey") ||
                !TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var entityType))
            {
                return;
            }

            configuredPrimaryKeys.TryAdd(
                entityType,
                methodName == "HasNoKey"
                    ? ConfiguredPrimaryKey.Unsupported
                    : AnalyzeKeyArgument(invocation.Arguments.FirstOrDefault()?.Value));
        }

        internal void ScanSyntaxTree(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (scannedTrees.Contains(syntaxTree))
                return;

            var root = syntaxTree.GetRoot(cancellationToken);
            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not ("HasKey" or "HasNoKey" or "HasQueryFilter"))
                {
                    continue;
                }

                if (semanticModel.GetOperation(invocationSyntax, cancellationToken) is IInvocationOperation invocation)
                {
                    RegisterConfiguredPrimaryKey(invocation);
                    RegisterQueryFilter(invocation);
                }
            }

            scannedTrees.Add(syntaxTree);
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
