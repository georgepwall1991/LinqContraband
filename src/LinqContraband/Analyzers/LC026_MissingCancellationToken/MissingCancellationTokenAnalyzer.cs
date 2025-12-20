using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

/// <summary>
/// Analyzes EF Core async calls to ensure CancellationToken is passed when available. Diagnostic ID: LC026
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC026";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Missing CancellationToken in async call";

    private static readonly LocalizableString MessageFormat =
        "The async method '{0}' is called without a CancellationToken. Pass a token to ensure the query can be cancelled.";

    private static readonly LocalizableString Description =
        "Always pass a CancellationToken to EF Core async operations to prevent resource waste when requests are cancelled.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC026_MissingCancellationToken.md");

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

        if (!method.Name.EndsWith("Async")) return;

        // Verify it's an EF Core method (or related library)
        if (!IsEfCoreMethod(method)) return;

        // Check if the method accepts a CancellationToken
        var ctParameter = method.Parameters.FirstOrDefault(p => 
            p.Type.Name == "CancellationToken" && 
            p.Type.ContainingNamespace?.ToString() == "System.Threading");

        if (ctParameter == null) return;

        // Find if an argument is passed for this parameter
        var ctArgument = invocation.Arguments.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.Parameter, ctParameter));

        if (ctArgument == null || ctArgument.IsImplicit || IsUsingDefault(ctArgument.Value))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns != null && (ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) || 
                             ns.StartsWith("System.Data.Entity", System.StringComparison.Ordinal));
    }

    private bool IsUsingDefault(IOperation operation)
    {
        var unwrapped = operation.UnwrapConversions();
        return unwrapped.Kind == OperationKind.DefaultValue || 
               (unwrapped is IPropertyReferenceOperation propRef && propRef.Property.Name == "None" && propRef.Property.ContainingType.Name == "CancellationToken");
    }
}
