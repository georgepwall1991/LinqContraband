using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC027_OrderByAfterPagination;

/// <summary>
/// Analyzes IQueryable operations to ensure OrderBy is not called after Skip or Take. Diagnostic ID: LC027
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrderByAfterPaginationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC027";
    private const string Category = "Correctness";
    private static readonly LocalizableString Title = "OrderBy called after Skip or Take";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' is called after 'Skip' or 'Take'. This results in sorting a subset of the data rather than the full set.";

    private static readonly LocalizableString Description =
        "Calling OrderBy after Skip or Take is usually a logic error. Sort the data before applying pagination.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC027_OrderByAfterPagination.md");

    private static readonly ImmutableHashSet<string> SortingMethods = ImmutableHashSet.Create(
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    );

    private static readonly ImmutableHashSet<string> PaginationMethods = ImmutableHashSet.Create(
        "Skip", "Take"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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

        if (!SortingMethods.Contains(method.Name)) return;

        // Check if receiver is IQueryable
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null || !receiver.Type.IsIQueryable()) return;

        // Walk up the chain to find Skip/Take
        if (HasPaginationUpstream(receiver))
        {
            var location = invocation.Syntax.GetLocation();
            if (invocation.Syntax is InvocationExpressionSyntax invocationSyntax &&
                invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                location = memberAccess.Name.GetLocation();

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, method.Name));
        }
    }

    private bool HasPaginationUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        while (current is IInvocationOperation inv)
        {
            if (PaginationMethods.Contains(inv.TargetMethod.Name))
            {
                return true;
            }

            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }

        return false;
    }
}
