using System.Collections.Concurrent;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Per-compilation memo of what each source helper method executes, so a helper called from many loops (or from
/// other helpers) is summarized once per remaining depth, keeping helper tracking linear in the size of the code.
/// </summary>
internal sealed class NPlusOneLooperHelperCache
{
    /// <summary>The summary stored for a helper that runs no database execution LC007 would report.</summary>
    internal const string NoExecution = "";

    private readonly ConcurrentDictionary<IMethodSymbol, string>[] summariesByDepth;
    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel?> semanticModels = new();

    public NPlusOneLooperHelperCache(Compilation compilation, int maxDepth)
    {
        Compilation = compilation;
        summariesByDepth = new ConcurrentDictionary<IMethodSymbol, string>[maxDepth];
        for (var i = 0; i < maxDepth; i++)
            summariesByDepth[i] = new ConcurrentDictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
    }

    public Compilation Compilation { get; }

    public bool TryGetSummary(IMethodSymbol method, int depth, out string queryMethodName)
    {
        return summariesByDepth[depth - 1].TryGetValue(method, out queryMethodName!);
    }

    public string StoreSummary(IMethodSymbol method, int depth, string queryMethodName)
    {
        return summariesByDepth[depth - 1].GetOrAdd(method, queryMethodName);
    }

    /// <summary>
    /// The semantic model for a helper's syntax tree, or <c>false</c> when the tree belongs to another compilation
    /// (a project reference in an IDE workspace), which is treated like metadata.
    /// </summary>
    public bool TryGetSemanticModel(SyntaxTree tree, out SemanticModel semanticModel)
    {
        var model = semanticModels.GetOrAdd(
            tree,
            t => Compilation.TryGetOwnedSemanticModel(t, out var owned) ? owned : null);
        semanticModel = model!;
        return model != null;
    }
}
