using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
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
    private static readonly ConditionalWeakTable<Compilation, NPlusOneLooperHelperCache> Caches = new();

    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel?> semanticModels = new();
    private readonly ConcurrentDictionary<ISymbol, bool> returnsEfQuery = new(SymbolEqualityComparer.Default);
    private NPlusOneLooperSourceTypeIndex? sourceTypes;

    public NPlusOneLooperHelperCache(Compilation compilation, int maxDepth)
    {
        Compilation = compilation;
        summariesByDepth = new ConcurrentDictionary<IMethodSymbol, string>[maxDepth];
        for (var i = 0; i < maxDepth; i++)
            summariesByDepth[i] = new ConcurrentDictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
    }

    public Compilation Compilation { get; }

    /// <summary>
    /// The one cache of a compilation, shared by every caller that only has an operation in hand and kept alive
    /// exactly as long as the compilation is.
    /// </summary>
    public static NPlusOneLooperHelperCache For(Compilation compilation)
    {
        return Caches.GetValue(compilation, c => new NPlusOneLooperHelperCache(c, NPlusOneLooperAnalysis.MaxHelperDepth));
    }

    /// <summary>Whether a method or property was already judged to return, on every path, a provably EF query.</summary>
    public bool TryGetReturnsEfQuery(ISymbol member, out bool returnsQuery)
    {
        return returnsEfQuery.TryGetValue(member, out returnsQuery);
    }

    public bool StoreReturnsEfQuery(ISymbol member, bool returnsQuery)
    {
        return returnsEfQuery.GetOrAdd(member, returnsQuery);
    }

    public bool TryGetSummary(IMethodSymbol method, int depth, out string queryMethodName)
    {
        return summariesByDepth[depth - 1].TryGetValue(method, out queryMethodName!);
    }

    public string StoreSummary(IMethodSymbol method, int depth, string queryMethodName)
    {
        return summariesByDepth[depth - 1].GetOrAdd(method, queryMethodName);
    }

    /// <summary>The compilation's concrete types by interface and base class, built on first use.</summary>
    public NPlusOneLooperSourceTypeIndex GetSourceTypes(CancellationToken cancellationToken)
    {
        var index = Volatile.Read(ref sourceTypes);
        if (index != null)
            return index;

        // Two threads may both build it; the index is immutable once built, so either copy will do.
        index = new NPlusOneLooperSourceTypeIndex(Compilation, cancellationToken);
        return Interlocked.CompareExchange(ref sourceTypes, index, null) ?? index;
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
