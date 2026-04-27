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
        return thresholdCache.GetValue(
            syntaxTree,
            tree => new StrongBox<int>(ReadThreshold(context.Options.AnalyzerConfigOptionsProvider, tree))).Value;
    }

    private static int ReadThreshold(AnalyzerConfigOptionsProvider optionsProvider, SyntaxTree syntaxTree)
    {
        var options = optionsProvider.GetOptions(syntaxTree);
        var threshold = DefaultThreshold;

        if (options.TryGetValue(ThresholdOptionKey, out var value) &&
            int.TryParse(value, out var configuredThreshold) &&
            configuredThreshold > 0)
        {
            threshold = configuredThreshold;
        }

        return threshold;
    }
}
