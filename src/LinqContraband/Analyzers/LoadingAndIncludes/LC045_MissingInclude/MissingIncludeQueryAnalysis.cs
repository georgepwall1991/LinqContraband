using System.Collections.Generic;
using System.Text;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private sealed class QueryChainInfo
    {
        public QueryChainInfo(
            IOperation querySource,
            INamedTypeSymbol entityType,
            INamedTypeSymbol contextType,
            HashSet<string> includedPrefixes,
            bool ignoresAutoIncludes,
            ISymbol? contextInstance,
            bool isNoTracking
        )
        {
            ContextInstance = contextInstance;
            IsNoTracking = isNoTracking;
            QuerySource = querySource;
            EntityType = entityType;
            ContextType = contextType;
            IncludedPrefixes = includedPrefixes;
            IgnoresAutoIncludes = ignoresAutoIncludes;
        }

        public IOperation QuerySource { get; }
        public INamedTypeSymbol EntityType { get; }
        public INamedTypeSymbol ContextType { get; }
        public HashSet<string> IncludedPrefixes { get; }
        public bool IgnoresAutoIncludes { get; }

        /// <summary>
        /// The local, parameter, field or property that holds the context the query runs on, or the
        /// context type itself for a query written inside the context. Null when it is anything else.
        /// </summary>
        public ISymbol? ContextInstance { get; }

        /// <summary>The chain opts out of tracking, so the change tracker does not fix its entities up.</summary>
        public bool IsNoTracking { get; }
    }

    private static bool TryAnalyzeQueryChain(
        IOperation querySource,
        CancellationToken cancellationToken,
        out QueryChainInfo queryInfo
    )
    {
        queryInfo = null!;

        // Allocated lazily: most materializer-named invocations are plain LINQ-to-objects
        // that fail the DbSet root proof.
        List<IInvocationOperation>? chain = null;
        var current = querySource;

        while (current != null)
        {
            current = UnwrapTranslatedQuery(current);

            if (current is IInvocationOperation invocation)
            {
                if (IsDbContextSetRoot(invocation))
                    break;

                if (!IsExactShapePreservingQueryStep(invocation))
                {
                    return false;
                }

                (chain ??= new List<IInvocationOperation>()).Add(invocation);
                current = GetQuerySource(invocation);
                continue;
            }

            // The query may be hoisted across a single-assignment local:
            // `var q = db.Orders.Where(...); var list = q.ToList();`
            if (current is ILocalReferenceOperation localReference)
            {
                var executableRoot = localReference.FindOwningExecutableRoot();
                if (
                    executableRoot != null
                    && LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        out var assignedValue,
                        cancellationToken
                    )
                )
                {
                    current = assignedValue;
                    continue;
                }

                return false;
            }

            break;
        }

        if (!TryGetDbSetRoot(current, out var entityType, out var contextType))
            return false;

        var includedPrefixes = new HashSet<string>(System.StringComparer.Ordinal);
        var semanticModel = querySource.SemanticModel;
        IncludePath? currentIncludePath = null;
        var ignoresAutoIncludes = false;
        var isNoTracking = false;

        // Root-first order so each ThenInclude sees the Include path it extends.
        if (chain != null)
        {
            chain.Reverse();
            foreach (var invocation in chain)
            {
                var methodName = invocation.TargetMethod.Name;
                if (methodName == "IgnoreAutoIncludes")
                    ignoresAutoIncludes = true;

                // The last one in the chain decides.
                if (methodName is "AsNoTracking" or "AsNoTrackingWithIdentityResolution")
                    isNoTracking = true;
                else if (methodName == "AsTracking")
                    isNoTracking = false;

                if (methodName != "Include" && methodName != "ThenInclude")
                    continue;

                // An Include we cannot parse (dynamic string, unresolved symbol) could cover any
                // navigation, so the whole query must stay quiet.
                if (
                    semanticModel == null
                    || !IncludePathParser.TryGetIncludePath(
                        invocation,
                        semanticModel,
                        currentIncludePath,
                        out var includePath
                    )
                )
                {
                    return false;
                }

                currentIncludePath = includePath;
                AddPathPrefixes(includePath, includedPrefixes);
            }
        }

        queryInfo = new QueryChainInfo(
            querySource,
            entityType,
            contextType,
            includedPrefixes,
            ignoresAutoIncludes,
            GetContextInstance(current),
            isNoTracking
        );
        return true;
    }

    private static void AddPathPrefixes(IncludePath path, HashSet<string> prefixes)
    {
        // Include(o => o.A.B) loads every entity along the path, so each prefix counts as loaded.
        var builder = new StringBuilder();
        foreach (var segment in path.Segments)
        {
            if (builder.Length > 0)
                builder.Append('.');
            builder.Append(segment.Name);
            prefixes.Add(builder.ToString());
        }
    }
}
