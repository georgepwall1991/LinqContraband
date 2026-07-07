using System.Collections.Immutable;
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
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC006_CartesianExplosion.md");

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
}
