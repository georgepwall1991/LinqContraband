using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

/// <summary>
/// Analyzes consecutive OrderBy or OrderByDescending calls that reset previous sorting. Diagnostic ID: LC005
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Calling OrderBy or OrderByDescending multiple times in a chain completely replaces
/// the previous sort order rather than adding a secondary sort. This is almost always a bug where the developer intended
/// to create a multi-level sort (e.g., by LastName then FirstName). Use ThenBy or ThenByDescending after the first OrderBy
/// to create proper multi-level sorting that preserves the original sort while adding sub-sorting for ties.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MultipleOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC005";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Multiple OrderBy calls";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' after an existing sort resets the order";

    private static readonly LocalizableString Description =
        "Consecutive OrderBy calls reset the sorting. Use ThenBy to chain sorts.";

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
        var method = invocation.TargetMethod;

        if (!IsOrderBy(method)) return;

        var receiver = invocation.Instance ??
                       (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        // Handle implicit conversions (e.g. boxing or interface casting)
        while (receiver is IConversionOperation conversion) receiver = conversion.Operand;

        if (receiver is IInvocationOperation previousInvocation)
        {
            var previousMethod = previousInvocation.TargetMethod;
            if (IsSortMethod(previousMethod))
            {
                var syntax = (InvocationExpressionSyntax)invocation.Syntax;
                var memberAccess = (MemberAccessExpressionSyntax)syntax.Expression;
                context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), method.Name));
            }
        }
    }

    private bool IsOrderBy(IMethodSymbol method)
    {
        return (method.Name == "OrderBy" || method.Name == "OrderByDescending") &&
               (method.ContainingType.Name == "Enumerable" || method.ContainingType.Name == "Queryable") &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private bool IsSortMethod(IMethodSymbol method)
    {
        return (method.Name == "OrderBy" || method.Name == "OrderByDescending" ||
                method.Name == "ThenBy" || method.Name == "ThenByDescending") &&
               (method.ContainingType.Name == "Enumerable" || method.ContainingType.Name == "Queryable") &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }
}
