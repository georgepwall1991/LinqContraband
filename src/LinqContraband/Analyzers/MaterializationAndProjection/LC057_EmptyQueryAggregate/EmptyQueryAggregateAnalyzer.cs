using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC057_EmptyQueryAggregate;

/// <summary>
/// Analyzes <c>Min</c>, <c>Max</c> and <c>Average</c> (and their EF Core <c>Async</c> forms) over a non-nullable value
/// on an EF Core query. Diagnostic ID: LC057
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> SQL <c>MIN</c>, <c>MAX</c> and <c>AVG</c> return <c>NULL</c> when no row matches.
/// EF Core cannot put <c>NULL</c> into a non-nullable result and throws <c>InvalidOperationException</c> ("Sequence
/// contains no elements"). Casting the selected value to its nullable type makes the query return <c>null</c>
/// instead. <c>Sum</c> is safe: EF Core returns zero.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class EmptyQueryAggregateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC057";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Min, Max or Average throws on an empty query";

    private static readonly LocalizableString MessageFormat =
        "'{0}' over non-nullable '{1}' throws when the EF Core query matches no rows; cast the value to '{1}?'";

    private static readonly LocalizableString Description =
        "SQL MIN, MAX and AVG return NULL for an empty set, and EF Core throws \"Sequence contains no elements\" when the result type cannot hold null. Cast the selected value to its nullable type, for example Max(x => (decimal?)x.Price), and handle the null result.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC057_EmptyQueryAggregate.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var provenance = new EfQueryProvenance(compilationContext.Compilation);
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, provenance),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, EfQueryProvenance provenance)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TryGetAggregatedValueType(invocation, out var valueType))
            return;

        if (IsInsideExpressionTree(invocation, context.CancellationToken))
            return;

        var source = invocation.GetInvocationReceiver();
        if (source == null || !provenance.IsProvablyEfQuery(source))
            return;

        if (HasNonEmptyGuard(invocation, source))
            return;

        var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location,
            invocation.TargetMethod.Name,
            valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// Matches <c>Queryable.Min/Max/Average</c> and EF Core's <c>MinAsync/MaxAsync/AverageAsync</c> with only a source,
    /// an optional selector and an optional cancellation token, and returns the value type being aggregated: the
    /// selector's result, or the element type of the source. Only a non-nullable value type qualifies.
    /// </summary>
    internal static bool TryGetAggregatedValueType(IInvocationOperation invocation, out ITypeSymbol valueType)
    {
        valueType = null!;
        var method = invocation.TargetMethod;
        if (method.Name is not ("Min" or "Max" or "Average" or "MinAsync" or "MaxAsync" or "AverageAsync"))
            return false;

        var containingType = method.ContainingType;
        var ns = containingType?.ContainingNamespace?.ToDisplayString();
        var isQueryable = containingType?.Name == "Queryable" && ns == "System.Linq";
        var isEfAsync = containingType?.Name == "EntityFrameworkQueryableExtensions" && ns == "Microsoft.EntityFrameworkCore";
        if (!(isQueryable && !method.Name.EndsWith("Async", System.StringComparison.Ordinal)) &&
            !(isEfAsync && method.Name.EndsWith("Async", System.StringComparison.Ordinal)))
        {
            return false;
        }

        ITypeSymbol? candidate = null;
        foreach (var argument in invocation.Arguments)
        {
            var parameter = argument.Parameter;
            if (parameter == null)
                return false;

            if (parameter.Ordinal == 0)
            {
                if (candidate == null && TryGetQueryableElementType(parameter.Type, out var elementType))
                    candidate = elementType;
                continue;
            }

            if (parameter.Name == "selector" && TryGetSelectorResultType(parameter.Type, out var resultType))
            {
                candidate = resultType;
                continue;
            }

            if (parameter.Type.Name == "CancellationToken")
                continue;

            // Comparer overloads and anything else unexpected.
            return false;
        }

        if (candidate is not { IsValueType: true } ||
            candidate.TypeKind == TypeKind.TypeParameter ||
            candidate.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        valueType = candidate;
        return true;
    }

    private static bool TryGetQueryableElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type is INamedTypeSymbol { Name: "IQueryable", TypeArguments.Length: 1 } named &&
            named.ContainingNamespace?.ToDisplayString() == "System.Linq")
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static bool TryGetSelectorResultType(ITypeSymbol type, out ITypeSymbol resultType)
    {
        resultType = null!;
        if (type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expression &&
            expression.TypeArguments[0] is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 2 } func)
        {
            resultType = func.TypeArguments[1];
            return true;
        }

        return false;
    }

    /// <summary>
    /// An aggregate inside another query's lambda is a correlated subquery that EF Core translates as part of that
    /// query. The rule leaves those alone.
    /// </summary>
    private static bool IsInsideExpressionTree(IInvocationOperation invocation, System.Threading.CancellationToken cancellationToken)
    {
        var semanticModel = invocation.SemanticModel;
        if (semanticModel == null)
            return true;

        for (var node = invocation.Syntax.Parent; node != null; node = node.Parent)
        {
            if (node is not AnonymousFunctionExpressionSyntax lambda)
                continue;

            if (semanticModel.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol
                {
                    Name: "Expression"
                } converted &&
                converted.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
            {
                return true;
            }
        }

        return false;
    }
}
