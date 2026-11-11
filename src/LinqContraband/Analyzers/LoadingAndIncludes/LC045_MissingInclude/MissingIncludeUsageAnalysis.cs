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
        MissingIncludeFlowCache flowCache,
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
                    operation is IInvocationOperation invocation
                    && TryCollectCollectionCallbackAccesses(
                        executableRoot,
                        invocation,
                        materializer,
                        resultLocal,
                        requireActiveCollection: false,
                        entityType,
                        entityTypes,
                        flowCache,
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
            flowCache,
            cancellationToken
        );
        if (accesses == null || !returnsCollection)
            return accesses;

        foreach (var operation in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                operation is not IInvocationOperation invocation
                || !TryCollectCollectionCallbackAccesses(
                    executableRoot,
                    invocation,
                    materializer,
                    resultLocal,
                    requireActiveCollection: true,
                    entityType,
                    entityTypes,
                    flowCache,
                    cancellationToken,
                    out var callbackAccesses
                )
            )
            {
                continue;
            }

            // The callback ran in its own graph, so a fix-up loop that already wrote the
            // navigation on every element has to be applied to its reads from out here.
            var satisfied = GetCollectionSatisfiedPathsAtInvocation(
                executableRoot,
                materializer,
                resultLocal,
                entityType,
                entityTypes,
                invocation,
                flowCache,
                cancellationToken
            );

            foreach (var callbackAccess in callbackAccesses)
            {
                if (!IsCoveredByCollectionWrite(callbackAccess, satisfied))
                    accesses.Add(callbackAccess);
            }
        }

        return accesses;
    }

    /// <summary>
    /// The navigation reads the callback of a per-element operator over the materialized
    /// collection performs on its element. The callback is a lambda, or a method group whose body
    /// is certain (see <see cref="TryGetMethodGroupCallback"/>).
    /// </summary>
    private static bool TryCollectCollectionCallbackAccesses(
        IOperation executableRoot,
        IInvocationOperation invocation,
        IInvocationOperation materializer,
        ILocalSymbol? resultLocal,
        bool requireActiveCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        MissingIncludeFlowCache flowCache,
        CancellationToken cancellationToken,
        out List<NavigationAccess> accesses
    )
    {
        accesses = null!;
        MethodGroupCallback methodGroup = default;
        if (
            !TryGetSupportedCollectionCallback(
                invocation,
                materializer,
                resultLocal,
                out var lambda
            )
            && !TryGetMethodGroupCallback(
                invocation,
                materializer,
                resultLocal,
                executableRoot,
                entityType,
                flowCache,
                cancellationToken,
                out methodGroup
            )
        )
        {
            return false;
        }

        if (
            requireActiveCollection
            && !IsMaterializedCollectionActiveAtInvocation(
                executableRoot,
                materializer,
                resultLocal!,
                entityType,
                entityTypes,
                invocation,
                flowCache,
                cancellationToken
            )
        )
        {
            return false;
        }

        return lambda != null
            ? TryCollectOriginAwareNavigationAccesses(
                executableRoot,
                lambda,
                lambda.Symbol.Parameters[0],
                entityType,
                entityTypes,
                flowCache,
                cancellationToken,
                out accesses
            )
            : TryCollectOriginAwareNavigationAccesses(
                executableRoot,
                methodGroup,
                invocation.SemanticModel,
                entityType,
                entityTypes,
                flowCache,
                cancellationToken,
                out accesses
            );
    }

    private static bool IsCoveredByCollectionWrite(
        NavigationAccess access,
        HashSet<string> collectionSatisfiedPaths
    )
    {
        foreach (var written in collectionSatisfiedPaths)
        {
            if (PathCovers(written, access.Path))
                return true;
        }

        return false;
    }
}
