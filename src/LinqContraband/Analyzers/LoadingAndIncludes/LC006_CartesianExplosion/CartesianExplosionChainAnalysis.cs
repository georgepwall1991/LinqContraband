using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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

    private static ImmutableArray<IInvocationOperation> CollectReceiverChainInvocations(IInvocationOperation outermostInvocation)
    {
        var builder = ImmutableArray.CreateBuilder<IInvocationOperation>();
        IOperation? current = outermostInvocation;

        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is IInvocationOperation invocation)
            {
                builder.Add(invocation);
                current = invocation.GetInvocationReceiver();
                continue;
            }

            // The Include chain can be split across a single-assignment local, e.g.
            // `var q = db.Users.AsSplitQuery(); q.Include(a).Include(b)` or
            // `var q = db.Users.Include(a); q.Include(b)`. Resolve the local back to its assigned
            // value so an effective AsSplitQuery() — or a sibling Include — on the prior statement
            // is still seen, closing both a false positive (split ignored) and a false negative
            // (siblings split across the local). The single-assignment-before-position guard keeps
            // reassigned or ambiguous locals conservative.
            if (current is ILocalReferenceOperation localReference)
            {
                var executableRoot = localReference.FindOwningExecutableRoot();
                if (executableRoot != null &&
                    LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        out var assignedValue))
                {
                    current = assignedValue;
                    continue;
                }
            }

            break;
        }

        builder.Reverse();
        return builder.ToImmutable();
    }

    private static bool HasRelevantQueryOperatorAncestor(IInvocationOperation invocation)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is not IInvocationOperation parentInvocation)
                continue;

            if (!IsRelevantQueryOperator(parentInvocation.TargetMethod))
                continue;

            if (InvocationUsesReceiverChain(parentInvocation.GetInvocationReceiver(), invocation))
                return true;
        }

        return false;
    }

    private static bool InvocationUsesReceiverChain(IOperation? current, IInvocationOperation target)
    {
        current = current?.UnwrapConversions();

        while (current != null)
        {
            if (ReferenceEquals(current, target))
                return true;

            if (current is IInvocationOperation invocation)
            {
                current = invocation.GetInvocationReceiver();
                continue;
            }

            break;
        }

        return false;
    }
}
