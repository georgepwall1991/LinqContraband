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
        if (!TryGetRemoveRangeEntity(invocation, out var entityType))
            return false;

        if (!TryGetRemoveRangeContext(invocation, cancellationToken, out var contextType))
            return evidence.IsEntityCoveredOnAnyContext(entityType);

        return evidence.IsCovered(contextType, entityType);
    }

    private static bool TryGetRemoveRangeEntity(IInvocationOperation invocation, out ITypeSymbol entityType)
    {
        entityType = null!;

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

        return entityType != null;
    }

    private static bool TryGetRemoveRangeContext(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        out ITypeSymbol contextType)
    {
        contextType = null!;
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
