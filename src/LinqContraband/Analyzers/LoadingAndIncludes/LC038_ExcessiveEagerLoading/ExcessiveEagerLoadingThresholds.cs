using System;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC038_ExcessiveEagerLoading;

public sealed partial class ExcessiveEagerLoadingAnalyzer
{
    private static int GetThreshold(OperationAnalysisContext context, ConditionalWeakTable<SyntaxTree, StrongBox<int>> thresholdCache)
    {
        var syntaxTree = context.Operation.Syntax.SyntaxTree;
        if (thresholdCache.TryGetValue(syntaxTree, out var cached))
            return cached.Value;

        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
        var threshold = DefaultThreshold;

        if (options.TryGetValue(ThresholdOptionKey, out var value) &&
            int.TryParse(value, out var configuredThreshold) &&
            configuredThreshold > 0)
        {
            threshold = configuredThreshold;
        }

        thresholdCache.Add(syntaxTree, new StrongBox<int>(threshold));
        return threshold;
    }
}
