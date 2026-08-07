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
        else
        {
            // Synchronous direct enumeration is only safe to analyse when the expression
            // remains statically IQueryable. Widened materialized IEnumerable results stay
            // diagnostic-only through the existing materializer path; widened direct-query
            // roots remain intentionally quiet.
            if (!forEach.Collection.Type.IsIQueryable())
                return;

            querySource = forEach.Collection;
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
        var entityFramework = compilation?.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
        );
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
