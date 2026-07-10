using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Collects every navigation path read from the materialized result inside the owning
    /// method. Origin-aware control flow keeps reads proven before an escape or reassignment,
    /// while subsequent reads on an escaped or ambiguously rebound origin stay conservative.
    /// Returns null only when the result binding or executable root cannot be proved.
    /// </summary>
    private static List<NavigationAccess>? CollectNavigationAccesses(
        IInvocationOperation materializer,
        bool returnsCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken
    )
    {
        var upwardParent = WalkUpThroughWrappers(materializer.Parent);
        if (upwardParent is IPropertyReferenceOperation)
        {
            // Inline access like db.Orders.First().Customer.Name — only provable for
            // single-entity materializers.
            return returnsCollection
                ? null
                : CollectInlineAccesses(
                    WalkUpThroughWrappers(materializer.Parent),
                    entityType,
                    entityTypes
                );
        }

        // db.Orders.FirstOrDefault()?.Customer.Name — the materializer is the guarded
        // receiver of a conditional access; the nav chain lives in WhenNotNull.
        if (
            upwardParent is IConditionalAccessOperation conditionalParent
            && conditionalParent.Operation.UnwrapConversions() == materializer
        )
        {
            return returnsCollection
                ? null
                : CollectInlineAccesses(
                    FindConditionalAccessEntryProperty(conditionalParent),
                    entityType,
                    entityTypes
                );
        }

        var resultLocal = FindVariableAssignment(materializer);
        if (resultLocal == null)
            return null;

        var executableRoot = materializer.FindOwningExecutableRoot();
        if (executableRoot == null)
            return null;

        var entityLocals = CollectEntityLocals(
            executableRoot,
            resultLocal,
            returnsCollection,
            cancellationToken
        );
        return CollectNavigationAccessesFromExecutableRoot(
            executableRoot,
            materializer,
            resultLocal,
            entityLocals,
            returnsCollection,
            entityType,
            entityTypes,
            flowGraphCache,
            cancellationToken
        );
    }
}
