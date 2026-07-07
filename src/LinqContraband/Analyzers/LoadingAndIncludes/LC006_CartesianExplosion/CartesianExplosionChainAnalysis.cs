using LinqContraband.Extensions;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    private static bool TryAnalyzeIncludeChain(
        IInvocationOperation outermostInvocation,
        out IncludeChainAnalysis analysis)
    {
        analysis = new IncludeChainAnalysis();
        var semanticModel = outermostInvocation.SemanticModel;
        if (semanticModel == null)
            return false;

        var invocations = CollectReceiverChainInvocations(outermostInvocation);
        IncludePath? currentIncludePath = null;
        var foundInclude = false;

        foreach (var invocation in invocations)
        {
            var methodName = invocation.TargetMethod.Name;
            if (!IsRelevantQueryOperator(invocation.TargetMethod))
                continue;

            if (methodName == "AsSplitQuery")
            {
                analysis.EffectiveQueryMode = QuerySplittingMode.Split;
                continue;
            }

            if (methodName == "AsSingleQuery")
            {
                analysis.EffectiveQueryMode = QuerySplittingMode.Single;
                continue;
            }

            if (!IncludePathParser.TryGetIncludePath(invocation, semanticModel, currentIncludePath, out var includePath))
                continue;

            foundInclude = true;
            currentIncludePath = includePath;
            analysis.AddIncludePath(includePath);
        }

        return foundInclude;
    }

}
