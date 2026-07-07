using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Collects every navigation path read from the materialized result inside the owning
    /// method. Returns null when the result (or an entity drawn from it) escapes — returned,
    /// passed as an argument, captured by a lambda, or stored outside a local — because a
    /// helper might explicitly load the navigation.
    /// </summary>
    private static List<NavigationAccess>? CollectNavigationAccesses(
        IInvocationOperation materializer,
        bool returnsCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        CancellationToken cancellationToken)
    {
        var upwardParent = WalkUpThroughWrappers(materializer.Parent);
        if (upwardParent is IPropertyReferenceOperation)
        {
            // Inline access like db.Orders.First().Customer.Name — only provable for
            // single-entity materializers.
            return returnsCollection
                ? null
                : CollectInlineAccesses(WalkUpThroughWrappers(materializer.Parent), entityType, entityTypes);
        }

        // db.Orders.FirstOrDefault()?.Customer.Name — the materializer is the guarded
        // receiver of a conditional access; the nav chain lives in WhenNotNull.
        if (upwardParent is IConditionalAccessOperation conditionalParent &&
            conditionalParent.Operation.UnwrapConversions() == materializer)
        {
            return returnsCollection
                ? null
                : CollectInlineAccesses(FindConditionalAccessEntryProperty(conditionalParent), entityType, entityTypes);
        }

        var resultLocal = FindVariableAssignment(materializer);
        if (resultLocal == null)
            return null;

        var executableRoot = materializer.FindOwningExecutableRoot();
        if (executableRoot == null)
            return null;

        // A reassigned result local could point at anything by the time it is read.
        if (LocalAssignmentCache.GetAssignments(executableRoot, resultLocal, cancellationToken).Count != 1)
            return null;

        var entityLocals = CollectEntityLocals(executableRoot, resultLocal, returnsCollection, cancellationToken);
        return CollectNavigationAccessesFromExecutableRoot(
            executableRoot,
            materializer,
            resultLocal,
            entityLocals,
            returnsCollection,
            entityType,
            entityTypes,
            cancellationToken);
    }
}
