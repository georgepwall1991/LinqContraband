using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeAnalyzer
{
    private static bool HasSubsequentSaveChangesInvocation(IInvocationOperation invocation, CancellationToken cancellationToken)
    {
        var root = invocation.FindOwningExecutableRoot();
        if (root == null)
            return false;

        var removeRangeReceiver = GetRemoveRangeContextReceiver(invocation);

        foreach (var candidate in root.Descendants().OfType<IInvocationOperation>())
        {
            if (candidate.Syntax.SpanStart <= invocation.Syntax.SpanStart ||
                !IsSaveChangesMethod(candidate.TargetMethod))
            {
                continue;
            }

            // A save in a mutually exclusive if/else or switch branch can never run after
            // this RemoveRange in the same execution, so it is not committing these
            // removals. try/catch deliberately still suppresses: the try may throw after
            // RemoveRange ran, and a catch-side save would commit the pending removals.
            if (AreMutuallyExclusiveBranches(invocation.Syntax, candidate.Syntax))
                continue;

            // A save on a provably different context never commits these removals. Proof
            // requires both receivers to resolve through single-assignment alias chains
            // to two different freshly-created locals; parameters, fields, and anything
            // reassigned could alias the same instance and stay conservatively suppressing.
            if (removeRangeReceiver != null &&
                TryResolveFreshContextLocal(removeRangeReceiver, root, cancellationToken, out var removeLocal) &&
                TryResolveFreshContextLocal(candidate.Instance, root, cancellationToken, out var saveLocal) &&
                !SymbolEqualityComparer.Default.Equals(removeLocal, saveLocal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The context that owns the pending removals: the receiver itself for
    /// DbContext.RemoveRange, or the DbSet member's instance for DbSet.RemoveRange. A DbSet
    /// arriving as a local/parameter has no visible owning context and resolves null.
    /// </summary>
    private static IOperation? GetRemoveRangeContextReceiver(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
            return receiver;

        return receiver is IMemberReferenceOperation memberReference ? memberReference.Instance : null;
    }

    /// <summary>
    /// Follows single-assignment local alias chains (var db2 = db1) back to the local whose
    /// one assignment is an object creation. Only such locals prove a distinct instance;
    /// anything else (parameters, fields, factory results, reassigned locals) could alias.
    /// </summary>
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

    private static bool IsSaveChangesMethod(IMethodSymbol method)
    {
        return method.Name is "SaveChanges" or "SaveChangesAsync" &&
               method.ContainingType.IsDbContext();
    }
}
