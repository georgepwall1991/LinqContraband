using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters;

/// <summary>
/// Analyzes usage of IgnoreQueryFilters which can bypass critical global security or logic filters. Diagnostic ID: LC021
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidIgnoreQueryFiltersAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC021";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid IgnoreQueryFilters";

    private static readonly LocalizableString MessageFormat =
        "Usage of 'IgnoreQueryFilters' can bypass critical global filters like multi-tenancy or soft-delete. Ensure this is intentional.";

    private static readonly LocalizableString Description =
        "IgnoreQueryFilters disables all global query filters for the current query, which might lead to unintended data access or incorrect business logic.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC021_AvoidIgnoreQueryFilters.md");

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

        if (method.Name != "IgnoreQueryFilters") return;

        // Verify it's an EF Core method
        if (!IsEfCoreMethod(method)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        return method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }
}
