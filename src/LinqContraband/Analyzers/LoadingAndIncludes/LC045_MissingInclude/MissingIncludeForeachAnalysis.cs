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
        OperationAnalysisContext context,
        System.Collections.Concurrent.ConcurrentDictionary<
            INamedTypeSymbol,
            HashSet<INamedTypeSymbol>
        > entityTypeCache,
        System.Collections.Concurrent.ConcurrentDictionary<
            INamedTypeSymbol,
            Dictionary<INamedTypeSymbol, HashSet<string>>
        > autoIncludeCache,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache
    )
    {
        if (context.Operation is not IForEachLoopOperation forEach || forEach.IsAsynchronous)
            return;

        var collection = forEach.Collection.UnwrapConversions();
        IOperation querySource;
        if (
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
            flowGraphCache,
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

    private static List<NavigationAccess>? CollectNavigationAccessesFromForeach(
        IForEachLoopOperation forEach,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
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
            flowGraphCache,
            cancellationToken,
            out var accesses
        )
            ? accesses
            : null;
    }
}
