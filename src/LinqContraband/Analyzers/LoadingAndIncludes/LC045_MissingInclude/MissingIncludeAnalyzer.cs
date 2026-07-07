using System.Collections.Immutable;
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
/// bugs ships: with lazy-loading proxies every access fires an extra query (the classic read-side
/// N+1); without proxies the navigation is silently null or an empty collection. Both are invisible
/// at compile time and only surface as production slowness or missing data.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC045";
    internal const string NavigationPathProperty = "NavigationPath";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title = "Missing Include: navigation accessed on materialized entity";

    private static readonly LocalizableString MessageFormat =
        "Navigation '{0}' on '{1}' is accessed after the query is materialized, but the query never Includes it. " +
        "Without Include it is null/empty (or triggers N+1 lazy loading). Add .Include() or project the data you need.";

    private static readonly LocalizableString Description =
        "Reading a navigation property that the query did not eagerly load is either a silent null/empty " +
        "collection (no lazy loading) or an N+1 query per access (lazy-loading proxies). " +
        "Add .Include()/.ThenInclude() for the navigation, or project exactly the data you need with Select().";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC045_MissingInclude.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            // The DbSet entity-type set is a per-context-type fact; cache it for the
            // compilation instead of re-walking the context's members per materializer.
            var entityTypeCache = new System.Collections.Concurrent.ConcurrentDictionary<INamedTypeSymbol, System.Collections.Generic.HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, entityTypeCache),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        System.Collections.Concurrent.ConcurrentDictionary<INamedTypeSymbol, System.Collections.Generic.HashSet<INamedTypeSymbol>> entityTypeCache)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!IsEntityMaterializer(invocation.TargetMethod, out var returnsCollection))
            return;

        if (!TryAnalyzeQueryChain(invocation, context.CancellationToken, out var query))
            return;

        var entityTypes = entityTypeCache.GetOrAdd(query.ContextType, static contextType => CollectDbSetEntityTypes(contextType));
        if (!entityTypes.Contains(query.EntityType))
            return;

        var accesses = CollectNavigationAccesses(
            invocation,
            returnsCollection,
            query.EntityType,
            entityTypes,
            context.CancellationToken);
        if (accesses == null || accesses.Count == 0)
            return;

        ReportMissingIncludeDiagnostics(context, invocation, query, accesses);
    }
}
