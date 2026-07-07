using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
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
}
