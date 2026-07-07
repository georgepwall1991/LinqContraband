using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeFixer
{
    private static async Task<bool> CanSafelyRewriteAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return false;

        var sourceType = semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type;
        if (!sourceType.IsIQueryable() && !sourceType.IsDbSet())
            return false;

        if (HasSubsequentSaveChangesInvocation(invocation, semanticModel, cancellationToken))
            return false;

        // Decline rather than emit an unsafe sync-over-async ExecuteDelete() when the call
        // sits in an async context but no awaitable ExecuteDeleteAsync overload is available.
        return DetermineRewriteMode(invocation, semanticModel) != RewriteMode.None;
    }

    private static IOperation? GetRemoveRangeContextReceiver(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
            return receiver;

        return receiver is IMemberReferenceOperation memberReference ? memberReference.Instance : null;
    }

    private static bool TryResolveFreshContextLocal(
        IOperation? receiver,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out ILocalSymbol? creationLocal)
    {
        creationLocal = null;
        var current = receiver?.UnwrapConversions();

        for (var depth = 0; depth < 16; depth++)
        {
            if (current is not ILocalReferenceOperation localReference)
                return false;

            var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);
            if (assignments.Count != 1)
                return false;

            var value = assignments[0].Value.UnwrapConversions();
            if (value is IObjectCreationOperation)
            {
                creationLocal = localReference.Local;
                return true;
            }

            current = value;
        }

        return false;
    }
}
