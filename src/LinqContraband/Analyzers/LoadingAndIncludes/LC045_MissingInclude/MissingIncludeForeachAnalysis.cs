using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static void AnalyzeForEach(
        MissingIncludeAnalysisContext context,
        System.Collections.Concurrent.ConcurrentDictionary<
            INamedTypeSymbol,
            HashSet<INamedTypeSymbol>
        > entityTypeCache,
        System.Collections.Concurrent.ConcurrentDictionary<
            INamedTypeSymbol,
            Dictionary<INamedTypeSymbol, HashSet<string>>
        > autoIncludeCache,
        MissingIncludeFlowCache flowCache
    )
    {
        if (context.Operation is not IForEachLoopOperation forEach)
            return;

        var collection = forEach.Collection.UnwrapConversions();
        IOperation querySource;
        if (forEach.IsAsynchronous)
        {
            // `await foreach` is the async spelling of the same loop: EF materializes the
            // entities one row at a time and a navigation read on the loop variable has the
            // same failure modes. Only a proven EF stream qualifies — an arbitrary
            // IAsyncEnumerable can already have loaded whatever it yields.
            if (!TryGetAsyncQueryStreamSource(forEach.Collection, collection, out querySource!))
                return;
        }
        else if (
            collection is IInvocationOperation invocation
            && IsEntityMaterializer(invocation, out var returnsCollection)
        )
        {
            if (!returnsCollection)
                return;

            querySource = GetQuerySource(invocation)?.UnwrapConversions()!;
            if (querySource == null)
                return;
        }
        else if (forEach.Collection.Type.IsIQueryable())
        {
            querySource = forEach.Collection;
        }
        else if (
            IsWidenedQueryableLocal(collection, forEach.Collection.Type, context.CancellationToken)
        )
        {
            // `IEnumerable<Order> source = db.Orders;` then `foreach (var o in source)` runs the
            // query exactly as iterating `db.Orders` does, and the same loop over
            // `source.ToList()` is already reported. Widening the static type changes nothing
            // about what EF does, so the chain proof — which resolves the local and still has to
            // reach a DbSet root — decides it, rather than the declared type.
            querySource = forEach.Collection;
        }
        else
        {
            return;
        }

        if (!TryAnalyzeQueryChain(querySource, context.CancellationToken, out var query))
            return;

        var entityTypes = EnsureRootEntityType(
            entityTypeCache.GetOrAdd(
                query.ContextType,
                static contextType => CollectDbSetEntityTypes(contextType)
            ),
            query.EntityType
        );

        var accesses = CollectNavigationAccessesFromForeach(
            forEach,
            query.EntityType,
            entityTypes,
            flowCache,
            context.CancellationToken
        );
        if (accesses == null || accesses.Count == 0)
            return;

        AddModelAutoIncludePrefixes(
            query,
            autoIncludeCache,
            context.Compilation,
            context.CancellationToken
        );
        ReportMissingIncludeDiagnostics(context, query.QuerySource, query, accesses);
    }

    /// <summary>
    /// A cheap gate deciding whether a widened name is worth asking the chain proof about: a local
    /// that holds a sequence, is not itself statically <c>IQueryable</c>, and was assigned a
    /// queryable exactly once before this point.
    /// Correctness is carried by the chain proof rather than here — it resolves the same local
    /// through the same single-assignment lookup and still has to reach a DbSet root, so a
    /// reassigned local, a conditionally bound one, a plain <c>List&lt;T&gt;</c> and a
    /// LINQ-to-objects query are all rejected there too. This gate exists so the walk is not run
    /// for every <c>foreach</c> over an ordinary collection.
    /// </summary>
    private static bool IsWidenedQueryableLocal(
        IOperation collection,
        ITypeSymbol? declaredType,
        CancellationToken cancellationToken
    )
    {
        return CouldHoldASequence(declaredType)
            && collection is ILocalReferenceOperation localReference
            && localReference.FindOwningExecutableRoot() is { } executableRoot
            && LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                localReference.Syntax.SpanStart,
                out var assignedValue,
                cancellationToken
            )
            && assignedValue.Type.IsIQueryable();
    }

    /// <summary>
    /// The query behind an <c>await foreach</c> source, when the stream is provably an EF
    /// Core query rather than an arbitrary async sequence. Two shapes qualify: the exact
    /// <c>EntityFrameworkQueryableExtensions.AsAsyncEnumerable</c> bridge over an
    /// <c>IQueryable&lt;T&gt;</c>, and a source that is still statically
    /// <c>IQueryable&lt;T&gt;</c> — <c>DbSet&lt;T&gt;</c> is directly awaitable because it
    /// implements both interfaces.
    /// </summary>
    private static bool TryGetAsyncQueryStreamSource(
        IOperation collectionOperation,
        IOperation unwrappedCollection,
        out IOperation? querySource
    )
    {
        querySource = null;

        if (collectionOperation.Type.IsIQueryable())
        {
            querySource = collectionOperation;
            return true;
        }

        if (unwrappedCollection is not IInvocationOperation invocation)
            return false;

        var compilation = invocation.SemanticModel?.Compilation;
        var entityFramework =
            compilation == null
                ? null
                : WellKnownSymbols.For(compilation).EntityFrameworkQueryableExtensions;
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (
            entityFramework == null
            || !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                entityFramework
            )
            || method.Name != "AsAsyncEnumerable"
            || method.Parameters.Length != 1
            || !method.Parameters[0].Type.IsIQueryable()
        )
        {
            return false;
        }

        querySource = GetQuerySource(invocation)?.UnwrapConversions();
        return querySource != null;
    }

    private static List<NavigationAccess>? CollectNavigationAccessesFromForeach(
        IForEachLoopOperation forEach,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        MissingIncludeFlowCache flowCache,
        CancellationToken cancellationToken
    )
    {
        var executableRoot = forEach.FindOwningExecutableRoot();
        if (executableRoot == null)
            return null;

        return TryCollectOriginAwareNavigationAccesses(
            executableRoot,
            forEach,
            entityType,
            entityTypes,
            flowCache,
            cancellationToken,
            out var accesses
        )
            ? accesses
            : null;
    }
}
