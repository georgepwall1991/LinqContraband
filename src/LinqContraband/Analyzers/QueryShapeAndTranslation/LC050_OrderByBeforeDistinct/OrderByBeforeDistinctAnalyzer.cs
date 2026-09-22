using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC050_OrderByBeforeDistinct;

/// <summary>
/// Analyzes queryable chains that sort before Distinct(). Diagnostic ID: LC050
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> SQL <c>DISTINCT</c> does not preserve row order, so EF Core drops an
/// <c>ORDER BY</c> that comes before <c>Distinct()</c> when no Skip/Take needs it. The query still runs, but
/// the results come back in whatever order the database chooses. Sort after <c>Distinct()</c> instead.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrderByBeforeDistinctAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC050";
    private const string Category = "Correctness";
    private static readonly LocalizableString Title = "OrderBy before Distinct is discarded";

    private static readonly LocalizableString MessageFormat =
        "'Distinct' discards the ordering from '{0}'; sort after Distinct() instead";

    private static readonly LocalizableString Description =
        "SQL DISTINCT does not preserve row order, so EF Core drops an OrderBy that comes before Distinct() unless Skip or Take needs it. Apply OrderBy after Distinct() so the results are actually sorted.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC050_OrderByBeforeDistinct.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var distinct = (IInvocationOperation)context.Operation;
        var method = distinct.TargetMethod.ReducedFrom ?? distinct.TargetMethod;
        if (method.Name != "Distinct" || method.ContainingType?.ToDisplayString() != "System.Linq.Queryable") return;
        if (distinct.Arguments.Length != 1) return;

        var sort = FindDiscardedSort(distinct.Arguments[0].Value);
        if (sort == null) return;

        var location = distinct.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : distinct.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, sort.TargetMethod.Name));
    }

    /// <summary>
    /// Walks back from Distinct's source through operators that neither need nor preserve the ordering and
    /// returns the OrderBy/OrderByDescending that starts the discarded sort, or null when there is none.
    /// </summary>
    private static IInvocationOperation? FindDiscardedSort(IOperation source)
    {
        var current = UnwrapQuery(source);

        while (current is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            var containingType = method.ContainingType?.ToDisplayString();

            if (containingType == "System.Linq.Queryable")
            {
                switch (method.Name)
                {
                    case "OrderBy":
                    case "OrderByDescending":
                        return IsInMemoryQueryable(invocation) ? null : invocation;
                    case "ThenBy":
                    case "ThenByDescending":
                    case "Where":
                    case "Select":
                        break;
                    default:
                        // Skip/Take/First and friends keep the ordering meaningful; AsQueryable and other operators
                        // leave the provider or the shape unproven.
                        return null;
                }
            }
            else if (!IsEfPassThrough(method))
            {
                return null;
            }

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null) return null;
            current = UnwrapQuery(receiver);
        }

        return null;
    }

    /// <summary>
    /// <c>list.AsQueryable()</c> runs on LINQ to Objects, where Distinct keeps first-seen order.
    /// </summary>
    private static bool IsInMemoryQueryable(IInvocationOperation sort)
    {
        IOperation? current = sort;
        while (current is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            if (method.Name == "AsQueryable" && method.ContainingType?.ToDisplayString() == "System.Linq.Queryable")
                return true;

            var receiver = invocation.GetInvocationReceiver();
            current = receiver == null ? null : UnwrapQuery(receiver);
        }

        return false;
    }

    private static IOperation UnwrapQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is ITranslatedQueryOperation query)
            current = query.Operation.UnwrapConversions();

        return current;
    }

    private static bool IsEfPassThrough(IMethodSymbol method)
    {
        if (method.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore") return false;

        return method.ContainingType?.Name switch
        {
            "EntityFrameworkQueryableExtensions" => method.Name is "AsNoTracking" or "AsNoTrackingWithIdentityResolution" or
                "AsTracking" or "TagWith" or "TagWithCallSite" or "IgnoreQueryFilters" or "IgnoreAutoIncludes" or
                "Include" or "ThenInclude",
            "RelationalQueryableExtensions" => method.Name is "AsSplitQuery" or "AsSingleQuery",
            _ => false
        };
    }
}
