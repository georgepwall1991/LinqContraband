using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Analyzes IQueryable operations (Skip, Last, Chunk) that require ordering but are called on unordered sequences. Diagnostic ID: LC015
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Operations like Skip, Last, and Chunk depend on a specific ordering to produce deterministic
/// results. Without an explicit OrderBy or OrderByDescending, the database may return results in any order, leading to
/// non-deterministic behavior in pagination, retrieval of last elements, or chunking operations. This can cause unpredictable
/// application behavior and difficult-to-reproduce bugs.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC015";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Deterministic Pagination: OrderBy required before Skip/Take";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' is called on an unordered IQueryable. Call 'OrderBy' or 'OrderByDescending' first to ensure deterministic results.";

    private static readonly LocalizableString MisplacedMessageFormat =
        "The method '{0}' is called after 'Skip' or 'Take'. This results in sorting a subset of the data rather than the full set.";

    private static readonly LocalizableString Description =
        "Pagination and Last operations on unordered IQueryables are non-deterministic. Sorting must happen before Skip/Take.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    public static readonly DiagnosticDescriptor MisplacedRule = new(
        DiagnosticId, "OrderBy after Skip/Take", MisplacedMessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    private static readonly ImmutableHashSet<string> PaginationMethods = ImmutableHashSet.Create(
        "Skip", "Take", "Last", "LastOrDefault", "Chunk"
    );

    private static readonly ImmutableHashSet<string> SortingMethods = ImmutableHashSet.Create(
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, MisplacedRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        bool isSorting = SortingMethods.Contains(method.Name);
        bool isPagination = PaginationMethods.Contains(method.Name);

        if (!isSorting && !isPagination) return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null || !receiver.Type.IsIQueryable()) return;

        // Case 1: OrderBy AFTER Skip/Take (was LC027)
        if (isSorting)
        {
            if (HasPaginationUpstream(receiver))
            {
                context.ReportDiagnostic(Diagnostic.Create(MisplacedRule, GetMethodLocation(invocation), method.Name));
            }
            return;
        }

        // Case 2: Skip/Take WITHOUT OrderBy (was original LC015)
        if (isPagination)
        {
            if (!HasOrderByUpstream(receiver))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, GetMethodLocation(invocation), method.Name));
            }
        }
    }

    private Location GetMethodLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax invocationSyntax &&
            invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.GetLocation();
        return invocation.Syntax.GetLocation();
    }

    private bool HasPaginationUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is IInvocationOperation inv)
        {
            if (PaginationMethods.Contains(inv.TargetMethod.Name)) return true;

            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }
        return false;
    }

    private bool HasOrderByUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        while (current != null)
            if (current is IInvocationOperation inv)
            {
                var method = inv.TargetMethod;

                if (SortingMethods.Contains(method.Name) && method.ReturnType.IsIQueryable()) return true;

                // Move "upstream"
                var next = inv.GetInvocationReceiver();
                if (next == null) return false;
                current = next.UnwrapConversions();
            }
            else
            {
                if (current.Type != null && IsOrderedQueryable(current.Type)) return true;
                return false;
            }

        return false;
    }

    private bool IsOrderedQueryable(ITypeSymbol type)
    {
        // Check if it implements IOrderedQueryable
        if (type.Name == "IOrderedQueryable" && type.ContainingNamespace?.ToString() == "System.Linq") return true;
        foreach (var i in type.AllInterfaces)
            if (i.Name == "IOrderedQueryable" && i.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        return false;
    }
}
