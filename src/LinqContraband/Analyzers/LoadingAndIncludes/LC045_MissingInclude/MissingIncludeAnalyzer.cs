using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

/// <summary>
/// Detects navigation properties accessed on materialized entities that the query never
/// eagerly loaded with Include. Diagnostic ID: LC045
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When a DbSet-rooted query is materialized (ToList, First, …)
/// and a navigation property of the result is then read without a matching Include, one of two
/// failure modes can ship: lazy-loading proxies can produce a read-side N+1; without another loading
/// mechanism or relationship fix-up, the navigation can remain null or empty. Both are invisible at
/// compile time and may surface only as production slowness or missing data.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC045";
    internal const string NavigationPathProperty = "NavigationPath";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title =
        "Missing Include: navigation accessed on materialized entity";

    private static readonly LocalizableString MessageFormat =
        "Navigation '{0}' on '{1}' is accessed after the query is materialized, but the query never Includes it. "
        + "Without another loading mechanism it can be null/empty (or trigger N+1 lazy loading). Add .Include() or project the data you need.";

    private static readonly LocalizableString Description =
        "Reading a navigation property that the query did not eagerly load can produce null/empty data "
        + "when no other loading mechanism applies, or an N+1 query per access with lazy-loading proxies. "
        + "Add .Include()/.ThenInclude() for the navigation, or project exactly the data you need with Select().";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC045_MissingInclude.md"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            // The DbSet entity-type set is a per-context-type fact; cache it for the
            // compilation instead of re-walking the context's members per materializer.
            var entityTypeCache = new System.Collections.Concurrent.ConcurrentDictionary<
                INamedTypeSymbol,
                System.Collections.Generic.HashSet<INamedTypeSymbol>
            >(SymbolEqualityComparer.Default);
            var flowGraphCache = new System.Runtime.CompilerServices.ConditionalWeakTable<
                IOperation,
                FlowGraphHolder
            >();
            compilationContext.RegisterOperationAction(
                operationContext =>
                    AnalyzeInvocation(operationContext, entityTypeCache, flowGraphCache),
                OperationKind.Invocation
            );
            compilationContext.RegisterOperationAction(
                operationContext =>
                    AnalyzeForEach(operationContext, entityTypeCache, flowGraphCache),
                OperationKind.Loop
            );
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        System.Collections.Concurrent.ConcurrentDictionary<
            INamedTypeSymbol,
            System.Collections.Generic.HashSet<INamedTypeSymbol>
        > entityTypeCache,
        System.Runtime.CompilerServices.ConditionalWeakTable<
            IOperation,
            FlowGraphHolder
        > flowGraphCache
    )
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!IsEntityMaterializer(invocation.TargetMethod, out var returnsCollection))
            return;

        var querySource = invocation.GetInvocationReceiver(unwrapConversions: false);
        if (
            querySource == null
            || !TryAnalyzeQueryChain(querySource, context.CancellationToken, out var query)
        )
            return;

        var entityTypes = EnsureRootEntityType(
            entityTypeCache.GetOrAdd(
                query.ContextType,
                static contextType => CollectDbSetEntityTypes(contextType)
            ),
            query.EntityType
        );

        var accesses = CollectNavigationAccesses(
            invocation,
            returnsCollection,
            query.EntityType,
            entityTypes,
            flowGraphCache,
            context.CancellationToken
        );
        if (accesses == null || accesses.Count == 0)
            return;

        ReportMissingIncludeDiagnostics(context, query.QuerySource, query, accesses);
    }

    private static System.Collections.Generic.HashSet<INamedTypeSymbol> EnsureRootEntityType(
        System.Collections.Generic.HashSet<INamedTypeSymbol> entityTypes,
        INamedTypeSymbol rootEntityType
    )
    {
        if (entityTypes.Contains(rootEntityType))
            return entityTypes;

        var expandedEntityTypes = new System.Collections.Generic.HashSet<INamedTypeSymbol>(
            entityTypes,
            SymbolEqualityComparer.Default
        );
        expandedEntityTypes.Add(rootEntityType);
        return expandedEntityTypes;
    }
}
