using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    private const int AnalyzerFullScanSyntaxTreeLimit = 64;

    public static PrimaryKeyCache CreateCache(Compilation compilation)
    {
        return new PrimaryKeyCache(
            compilation,
            allowFullScan: true,
            useConventionFallbackWhenConfigurationUnknown: true);
    }

    public static PrimaryKeyCache CreateAnalyzerCache(Compilation compilation)
    {
        var allowFullScan = compilation.SyntaxTrees.Take(AnalyzerFullScanSyntaxTreeLimit + 1).Count() <= AnalyzerFullScanSyntaxTreeLimit;
        return new PrimaryKeyCache(
            compilation,
            allowFullScan,
            useConventionFallbackWhenConfigurationUnknown: allowFullScan);
    }

    public static string? TryFindSafePrimaryKey(
        ITypeSymbol entityType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        return CreateCache(compilation).TryFindSafePrimaryKey(entityType, cancellationToken);
    }

    internal sealed class PrimaryKeyCache
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
                    memberAccess.Name.Identifier.ValueText != "HasKey")
                {
                    continue;
                }

                if (semanticModel.GetOperation(invocationSyntax, cancellationToken) is IInvocationOperation invocation)
                    RegisterConfiguredPrimaryKey(invocation);
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

    private static void BuildConfiguredPrimaryKeys(
        Compilation compilation,
        PrimaryKeyCache primaryKeyCache,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = compilation.GetSemanticModel(tree);
            primaryKeyCache.EnsureSyntaxTreeScanned(tree, semanticModel, cancellationToken);
        }
    }

    private static bool TryGetEntityTypeBuilderEntity(ITypeSymbol? receiverType, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (receiverType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.TypeArguments.Length != 1 ||
            !IsEntityTypeBuilder(namedType))
        {
            return false;
        }

        entityType = namedType.TypeArguments[0];
        return true;
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol namedType)
    {
        if (namedType.Name != "EntityTypeBuilder")
            return false;

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        return namespaceName is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static ConfiguredPrimaryKey AnalyzeKeyArgument(IOperation? keyArgument)
    {
        if (keyArgument?.UnwrapConversions() is not IAnonymousFunctionOperation lambda)
            return ConfiguredPrimaryKey.Unsupported;

        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOperation)
            body = returnOperation.ReturnedValue;

        if (body != null && TryGetLambdaPropertyName(body.UnwrapConversions(), lambda, out var propertyName))
            return ConfiguredPrimaryKey.Single(propertyName);

        return ConfiguredPrimaryKey.Unsupported;
    }

    private static bool TryGetLambdaPropertyName(
        IOperation operation,
        IAnonymousFunctionOperation lambda,
        out string propertyName)
    {
        propertyName = string.Empty;

        if (operation is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not IParameterReferenceOperation parameterReference ||
            !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
        {
            return false;
        }

        propertyName = propertyReference.Property.Name;
        return true;
    }

    private readonly struct ConfiguredPrimaryKey
    {
        private ConfiguredPrimaryKey(bool isConfigured, string? propertyName)
        {
            IsConfigured = isConfigured;
            PropertyName = propertyName;
        }

        public bool IsConfigured { get; }

        public string? PropertyName { get; }

        public static ConfiguredPrimaryKey NotConfigured => new(false, null);

        public static ConfiguredPrimaryKey Unsupported => new(true, null);

        public static ConfiguredPrimaryKey Single(string propertyName) => new(true, propertyName);
    }
}
