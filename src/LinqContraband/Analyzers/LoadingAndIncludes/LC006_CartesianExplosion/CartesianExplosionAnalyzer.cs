using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

/// <summary>
/// Analyzes Entity Framework queries that Include multiple collection navigations, causing Cartesian product data duplication. Diagnostic ID: LC006
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When multiple collection navigations are loaded in a single query using Include(),
/// Entity Framework generates a SQL query with multiple JOINs that creates a Cartesian product. This causes geometric
/// data duplication where the result set size equals the product of all collection sizes (e.g., 10 Orders with 5 Items
/// each and 3 Payments each returns 150 rows instead of 18). This wastes bandwidth, memory, and database resources.
/// Use AsSplitQuery() to separate into distinct SQL queries or manually load collections separately.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class CartesianExplosionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC006";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Cartesian Explosion Risk: Multiple Collection Includes";

    private static readonly LocalizableString MessageFormat =
        "Including sibling collections ('{0}') in a single query can cause cartesian explosion. Use AsSplitQuery().";

    private static readonly LocalizableString Description =
        "Loading multiple collections in a single query causes geometric data duplication. Use .AsSplitQuery() to separate them into distinct SQL queries.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!IsRelevantQueryOperator(invocation.TargetMethod))
            return;

        if (HasRelevantQueryOperatorAncestor(invocation))
            return;

        if (!TryAnalyzeIncludeChain(invocation, out var chain))
            return;

        if (chain.EffectiveQueryMode == QuerySplittingMode.Split)
            return;

        if (chain.TryGetRiskySiblingCollections(out var siblings))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), string.Join("', '", siblings)));
        }
    }

    private static bool IsRelevantQueryOperator(IMethodSymbol method)
    {
        return method.Name is "Include" or "ThenInclude" or "AsSplitQuery" or "AsSingleQuery" &&
               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private enum QuerySplittingMode
    {
        None,
        Split,
        Single
    }

    private readonly struct NavigationSegment
    {
        public NavigationSegment(string name, bool isCollection)
        {
            Name = name;
            IsCollection = isCollection;
        }

        public string Name { get; }
        public bool IsCollection { get; }
    }

    private sealed class IncludePath
    {
        public IncludePath(ImmutableArray<NavigationSegment> segments)
        {
            Segments = segments;
        }

        public ImmutableArray<NavigationSegment> Segments { get; }

        public string Key => string.Join(".", Segments.Select(segment => segment.Name));

        public IncludePath Append(IncludePath childPath)
        {
            return new IncludePath(Segments.AddRange(childPath.Segments));
        }
    }

    private sealed class IncludeChainAnalysis
    {
        private readonly System.Collections.Generic.List<IncludePath> includePaths = new();

        public QuerySplittingMode EffectiveQueryMode { get; set; }

        public void AddIncludePath(IncludePath path)
        {
            if (path.Segments.Length > 0)
                includePaths.Add(path);
        }

        public bool TryGetRiskySiblingCollections(out ImmutableArray<string> siblings)
        {
            var seenIncludePaths = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(System.StringComparer.Ordinal);
            var groupSets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>(System.StringComparer.Ordinal);

            foreach (var path in includePaths)
            {
                if (!seenIncludePaths.Add(path.Key))
                    continue;

                var parent = new System.Collections.Generic.List<string>();
                foreach (var segment in path.Segments)
                {
                    if (segment.IsCollection)
                    {
                        var parentKey = string.Join(".", parent);
                        if (!groups.TryGetValue(parentKey, out var group))
                        {
                            group = new System.Collections.Generic.List<string>();
                            groups[parentKey] = group;
                            groupSets[parentKey] = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                        }

                        if (groupSets[parentKey].Add(segment.Name))
                            group.Add(segment.Name);
                    }

                    parent.Add(segment.Name);
                }
            }

            foreach (var group in groups.Values)
            {
                if (group.Count > 1)
                {
                    siblings = group.ToImmutableArray();
                    return true;
                }
            }

            siblings = ImmutableArray<string>.Empty;
            return false;
        }
    }
}
