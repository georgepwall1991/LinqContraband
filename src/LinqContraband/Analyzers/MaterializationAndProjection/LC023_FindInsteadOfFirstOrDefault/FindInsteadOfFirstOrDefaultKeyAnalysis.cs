using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    public static PrimaryKeyCache CreateCache(Compilation compilation)
    {
        return new PrimaryKeyCache(compilation);
    }

    public static string? TryFindSafePrimaryKey(
        ITypeSymbol entityType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        return CreateCache(compilation).TryFindSafePrimaryKey(entityType, cancellationToken);
    }

    /// <summary>
    /// Registers every EF key and query-filter configuration in the compilation. Only trees
    /// whose text mentions HasKey/HasNoKey/HasQueryFilter get a semantic model, so the cost
    /// is a text search per tree (cached per tree across compilations) plus binding the
    /// model-configuration code, not binding the whole compilation.
    /// </summary>
    private static void BuildConfiguredPrimaryKeys(
        Compilation compilation,
        PrimaryKeyCache primaryKeyCache,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MayContainModelConfiguration(tree, cancellationToken))
                continue;

            var semanticModel = compilation.GetSemanticModel(tree);
            primaryKeyCache.ScanSyntaxTree(tree, semanticModel, cancellationToken);
        }
    }
}
