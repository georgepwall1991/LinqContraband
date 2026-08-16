using System.Threading;
using LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeAnalyzer
{
    internal static bool BypassesTrackedDeletePipeline(
        IInvocationOperation invocation,
        TrackedDeletePipelineEvidence evidence,
        CancellationToken cancellationToken)
    {
        if (!TryGetRemoveRangeTarget(invocation, cancellationToken, out var entityType, out var contextType))
            return false;

        return evidence.IsCovered(contextType, entityType);
    }

    private static bool TryGetRemoveRangeTarget(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        out ITypeSymbol entityType,
        out ITypeSymbol contextType)
    {
        entityType = null!;
        contextType = null!;

        if (invocation.Arguments.Length == 1)
        {
            var sourceType = invocation.Arguments[0].Value.UnwrapConversions().Type;
            if (sourceType is INamedTypeSymbol named && named.TypeArguments.Length == 1)
                entityType = named.TypeArguments[0];
        }

        if (entityType == null &&
            invocation.TargetMethod.ContainingType is INamedTypeSymbol containing &&
            containing.IsDbSet() &&
            containing.TypeArguments.Length == 1)
        {
            entityType = containing.TypeArguments[0];
        }

        if (entityType == null)
            return false;

        var executableRoot = invocation.FindOwningExecutableRoot();
        var origin = invocation.TargetMethod.ContainingType.IsDbContext()
            ? invocation.Instance
            : invocation.GetInvocationReceiver();

        return ExecuteDeleteBypassesTrackedDeleteAnalyzer.TryResolveContextType(
            origin,
            executableRoot,
            cancellationToken,
            out contextType);
    }
}
