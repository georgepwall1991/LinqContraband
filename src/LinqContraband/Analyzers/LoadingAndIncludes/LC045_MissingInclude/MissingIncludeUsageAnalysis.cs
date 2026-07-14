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

        var executableRoot = materializer.FindOwningExecutableRoot();
        if (executableRoot == null)
            return null;

        var resultLocal = FindVariableAssignment(materializer);
        if (resultLocal == null)
        {
            if (!returnsCollection)
                return null;

            var inlineAccesses = new List<NavigationAccess>();
            foreach (var operation in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    operation is not IInvocationOperation invocation
                    || !TryGetSupportedCollectionCallback(
                        invocation,
                        materializer,
                        resultLocal,
                        out var callback
                    )
                )
                {
                    continue;
                }

                if (
                    TryCollectOriginAwareNavigationAccesses(
                        executableRoot,
                        callback,
                        callback.Symbol.Parameters[0],
                        entityType,
                        entityTypes,
                        flowGraphCache,
                        cancellationToken,
                        out var callbackAccesses
                    )
                )
                {
                    inlineAccesses.AddRange(callbackAccesses);
                }
            }

            return inlineAccesses;
        }

        var entityLocals = CollectEntityLocals(
            executableRoot,
            resultLocal,
            returnsCollection,
            cancellationToken
        );
        var accesses = CollectNavigationAccessesFromExecutableRoot(
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
        if (accesses == null || !returnsCollection)
            return accesses;

        foreach (var operation in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                operation is not IInvocationOperation invocation
                || !TryGetSupportedCollectionCallback(
                    invocation,
                    materializer,
                    resultLocal,
                    out var callback
                )
                || !IsMaterializedCollectionActiveAtInvocation(
                    executableRoot,
                    materializer,
                    resultLocal,
                    entityType,
                    entityTypes,
                    invocation,
                    flowGraphCache,
                    cancellationToken
                )
            )
            {
                continue;
            }

            if (
                TryCollectOriginAwareNavigationAccesses(
                    executableRoot,
                    callback,
                    callback.Symbol.Parameters[0],
                    entityType,
                    entityTypes,
                    flowGraphCache,
                    cancellationToken,
                    out var callbackAccesses
                )
            )
            {
                accesses.AddRange(callbackAccesses);
            }
        }

        return accesses;
    }
}
