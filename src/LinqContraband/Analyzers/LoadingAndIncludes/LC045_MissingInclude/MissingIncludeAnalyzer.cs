using System.Collections.Immutable;
using System.Linq;
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
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!IsEntityMaterializer(invocation.TargetMethod, out var returnsCollection))
            return;

        if (!TryAnalyzeQueryChain(invocation, context.CancellationToken, out var query))
            return;

        var entityTypes = CollectDbSetEntityTypes(query.ContextType);
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

        // First access site per distinct missing path.
        var firstAccessByPath = new System.Collections.Generic.Dictionary<string, NavigationAccess>(System.StringComparer.Ordinal);
        foreach (var access in accesses)
        {
            if (query.IncludedPrefixes.Contains(access.Path))
                continue;

            if (!firstAccessByPath.TryGetValue(access.Path, out var existing) ||
                access.Syntax.SpanStart < existing.Syntax.SpanStart)
            {
                firstAccessByPath[access.Path] = access;
            }
        }

        if (firstAccessByPath.Count == 0)
            return;

        var materializerLocation = new[] { invocation.Syntax.GetLocation() };

        foreach (var pair in firstAccessByPath.OrderBy(entry => entry.Value.Syntax.SpanStart))
        {
            // Only maximal paths: fixing "Customer.Address" eagerly loads "Customer" too.
            if (HasLongerMissingPath(firstAccessByPath, pair.Key))
                continue;

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(NavigationPathProperty, pair.Key);

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                pair.Value.Syntax.GetLocation(),
                additionalLocations: materializerLocation,
                properties: properties,
                pair.Key,
                query.EntityType.Name));
        }
    }

    private static bool HasLongerMissingPath(
        System.Collections.Generic.Dictionary<string, NavigationAccess> missingPaths,
        string path)
    {
        foreach (var candidate in missingPaths.Keys)
        {
            if (candidate.Length > path.Length &&
                candidate.StartsWith(path, System.StringComparison.Ordinal) &&
                candidate[path.Length] == '.')
            {
                return true;
            }
        }

        return false;
    }
}
