using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private const int MaxPreloadedPathDepth = 4;

    /// <summary>
    /// A table loaded whole into the same context before the query runs: <c>db.Customers.Load();</c>.
    /// When EF Core materializes an order on a tracking query it fixes <c>order.Customer</c> up to the
    /// customer it already tracks, and a lazy-loading proxy treats that reference as loaded. So every
    /// reference navigation to a preloaded entity type counts as included, along each path whose
    /// earlier steps are loaded too (<c>Customer.Address</c> with both tables preloaded,
    /// <c>Items.Product</c> with <c>Include(o =&gt; o.Items)</c>).
    ///
    /// Collections are left out on purpose: fix-up fills <c>customer.Orders</c> but does not mark it
    /// loaded, so a lazy-loading proxy still queries once per customer.
    /// </summary>
    private static void AddPreloadedReferencePrefixes(
        QueryChainInfo query,
        IOperation queryOperation,
        HashSet<INamedTypeSymbol> entityTypes
    )
    {
        if (query.IsNoTracking || query.ContextInstance == null)
            return;

        var preloaded = FindPreloadedEntityTypes(queryOperation, query.ContextInstance);
        if (preloaded == null)
            return;

        AddPreloadedReferencePaths(
            query.EntityType,
            string.Empty,
            1,
            preloaded,
            entityTypes,
            query.IncludedPrefixes
        );
    }

    /// <summary>
    /// The entity types of the <c>Load()</c>/<c>LoadAsync()</c> statements that are certain to have
    /// run before <paramref name="queryOperation"/>: earlier statements of a block that encloses it,
    /// within the same lambda or local function. Null when there are none, or when the method clears
    /// the change tracker.
    /// </summary>
    private static HashSet<INamedTypeSymbol>? FindPreloadedEntityTypes(
        IOperation queryOperation,
        ISymbol contextInstance
    )
    {
        HashSet<INamedTypeSymbol>? preloaded = null;
        var child = queryOperation;
        IOperation outermost = queryOperation;
        for (var current = queryOperation.Parent; current != null; current = current.Parent)
        {
            // A local function can be called before the statements that precede its declaration,
            // and a lambda's enclosing statements belong to another body.
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;

            if (current is IBlockOperation block)
            {
                foreach (var statement in block.Operations)
                {
                    if (statement == child)
                        break;

                    if (TryGetPreloadedEntityType(statement, contextInstance, out var entityType))
                    {
                        preloaded ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                        preloaded.Add(entityType);
                    }
                }
            }

            child = current;
            outermost = current;
        }

        if (preloaded == null)
            return null;

        foreach (var operation in outermost.DescendantsAndSelf())
        {
            if (
                operation is IInvocationOperation
                {
                    TargetMethod: { Name: "Clear", ContainingType.Name: "ChangeTracker" }
                }
            )
            {
                return null;
            }
        }

        return preloaded;
    }

    /// <summary>
    /// <c>db.Customers.Load();</c> or <c>await db.Customers.LoadAsync(ct);</c> as a statement of its
    /// own, on the whole set: nothing between the set and <c>Load</c> may filter it or turn tracking
    /// off.
    /// </summary>
    private static bool TryGetPreloadedEntityType(
        IOperation statement,
        ISymbol contextInstance,
        out INamedTypeSymbol entityType
    )
    {
        entityType = null!;
        if (statement is not IExpressionStatementOperation { Operation: var expression })
            return false;

        expression = expression.UnwrapConversions();
        if (expression is IAwaitOperation awaited)
            expression = awaited.Operation.UnwrapConversions();

        if (
            expression is IInvocationOperation { TargetMethod.Name: "ConfigureAwait" } configureAwait
            && configureAwait.Instance != null
        )
        {
            expression = configureAwait.Instance.UnwrapConversions();
        }

        if (
            expression
            is not IInvocationOperation { TargetMethod.Name: "Load" or "LoadAsync" } load
        )
        {
            return false;
        }

        var source = GetQuerySource(load)?.UnwrapConversions();
        while (
            source is IInvocationOperation step
            && !IsDbContextSetRoot(step)
            && step.TargetMethod.Name
                is "Include"
                    or "ThenInclude"
                    or "AsTracking"
                    or "AsSplitQuery"
                    or "AsSingleQuery"
                    or "OrderBy"
                    or "OrderByDescending"
                    or "ThenBy"
                    or "ThenByDescending"
                    or "TagWith"
                    or "TagWithCallSite"
        )
        {
            source = GetQuerySource(step)?.UnwrapConversions();
        }

        return TryGetDbSetRoot(source, out entityType, out _)
            && SymbolEqualityComparer.Default.Equals(GetContextInstance(source), contextInstance);
    }

    /// <summary>
    /// What holds the context a set root is read from: <c>db</c> in <c>db.Orders</c> or
    /// <c>db.Set&lt;Order&gt;()</c>. Two roots with the same symbol read the same context, as long as
    /// it is a local, a parameter, or a member of <c>this</c>.
    /// </summary>
    private static ISymbol? GetContextInstance(IOperation? setRoot)
    {
        var instance = setRoot switch
        {
            IInvocationOperation invocation => invocation.Instance,
            IMemberReferenceOperation memberReference => memberReference.Instance,
            _ => null,
        };

        return instance?.UnwrapConversions() switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field =>
                field.Field,
            IPropertyReferenceOperation
            {
                Instance: null or IInstanceReferenceOperation
            } property => property.Property,
            IInstanceReferenceOperation self => self.Type,
            _ => null,
        };
    }

    private static void AddPreloadedReferencePaths(
        INamedTypeSymbol entityType,
        string prefix,
        int depth,
        HashSet<INamedTypeSymbol> preloaded,
        HashSet<INamedTypeSymbol> entityTypes,
        HashSet<string> includedPrefixes
    )
    {
        for (var type = entityType; type != null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (
                    member is not IPropertySymbol property
                    || !TryGetNavigationTarget(
                        property,
                        entityTypes,
                        out var target,
                        out var isCollection
                    )
                )
                {
                    continue;
                }

                var path = CombineNavigationPath(prefix, property.Name);
                if (!isCollection && preloaded.Contains(target))
                    includedPrefixes.Add(path);
                else if (!includedPrefixes.Contains(path))
                    continue;

                if (depth < MaxPreloadedPathDepth)
                {
                    AddPreloadedReferencePaths(
                        target,
                        path,
                        depth + 1,
                        preloaded,
                        entityTypes,
                        includedPrefixes
                    );
                }
            }
        }
    }
}
