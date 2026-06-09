using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Operators that keep the query shaped as "a stream of the root entity". Anything else
    /// (Select, Join, GroupBy, custom extensions, …) reshapes the result or may add its own
    /// loading behaviour, so the whole query is conservatively out of scope.
    /// </summary>
    private static readonly ImmutableHashSet<string> ShapePreservingOperators = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "AsSplitQuery",
        "AsSingleQuery",
        "TagWith",
        "TagWithCallSite",
        "IgnoreQueryFilters",
        "Include",
        "ThenInclude");

    private sealed class QueryChainInfo
    {
        public QueryChainInfo(INamedTypeSymbol entityType, INamedTypeSymbol contextType, HashSet<string> includedPrefixes)
        {
            EntityType = entityType;
            ContextType = contextType;
            IncludedPrefixes = includedPrefixes;
        }

        public INamedTypeSymbol EntityType { get; }
        public INamedTypeSymbol ContextType { get; }
        public HashSet<string> IncludedPrefixes { get; }
    }

    private static bool IsEntityMaterializer(IMethodSymbol method, out bool returnsCollection)
    {
        returnsCollection = false;

        switch (method.Name)
        {
            case "ToList":
            case "ToListAsync":
            case "ToArray":
            case "ToArrayAsync":
                returnsCollection = true;
                break;

            case "First":
            case "FirstAsync":
            case "FirstOrDefault":
            case "FirstOrDefaultAsync":
            case "Single":
            case "SingleAsync":
            case "SingleOrDefault":
            case "SingleOrDefaultAsync":
            case "Last":
            case "LastAsync":
            case "LastOrDefault":
            case "LastOrDefaultAsync":
                break;

            default:
                return false;
        }

        return method.IsFrameworkMethod();
    }

    private static bool TryAnalyzeQueryChain(
        IInvocationOperation materializer,
        CancellationToken cancellationToken,
        out QueryChainInfo queryInfo)
    {
        queryInfo = null!;

        var chain = new List<IInvocationOperation>();
        var current = materializer.GetInvocationReceiver();

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (!ShapePreservingOperators.Contains(invocation.TargetMethod.Name) ||
                    !invocation.TargetMethod.IsFrameworkMethod())
                {
                    return false;
                }

                chain.Add(invocation);
                current = invocation.GetInvocationReceiver();
                continue;
            }

            // The query may be hoisted across a single-assignment local:
            // `var q = db.Orders.Where(...); var list = q.ToList();`
            if (current is ILocalReferenceOperation localReference)
            {
                var executableRoot = localReference.FindOwningExecutableRoot();
                if (executableRoot != null &&
                    LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        out var assignedValue,
                        cancellationToken))
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
        var semanticModel = materializer.SemanticModel;
        IncludePath? currentIncludePath = null;

        // Root-first order so each ThenInclude sees the Include path it extends.
        chain.Reverse();
        foreach (var invocation in chain)
        {
            var methodName = invocation.TargetMethod.Name;
            if (methodName != "Include" && methodName != "ThenInclude")
                continue;

            // An Include we cannot parse (dynamic string, unresolved symbol) could cover any
            // navigation, so the whole query must stay quiet.
            if (semanticModel == null ||
                !IncludePathParser.TryGetIncludePath(invocation, semanticModel, currentIncludePath, out var includePath))
            {
                return false;
            }

            currentIncludePath = includePath;
            AddPathPrefixes(includePath, includedPrefixes);
        }

        queryInfo = new QueryChainInfo(entityType, contextType, includedPrefixes);
        return true;
    }

    private static bool TryGetDbSetRoot(
        IOperation? operation,
        out INamedTypeSymbol entityType,
        out INamedTypeSymbol contextType)
    {
        entityType = null!;
        contextType = null!;

        if (operation is not IMemberReferenceOperation memberReference)
            return false;
        if (operation is not (IPropertyReferenceOperation or IFieldReferenceOperation))
            return false;

        var rootType = memberReference.Type;
        if (rootType == null || !rootType.IsDbSet())
            return false;

        var contextCandidate = memberReference.Instance?.Type as INamedTypeSymbol ??
                               memberReference.Member.ContainingType;
        if (contextCandidate == null || !contextCandidate.IsDbContext())
            return false;

        if (rootType is not INamedTypeSymbol namedRoot ||
            namedRoot.TypeArguments.Length != 1 ||
            namedRoot.TypeArguments[0] is not INamedTypeSymbol element)
        {
            return false;
        }

        entityType = element;
        contextType = contextCandidate;
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
