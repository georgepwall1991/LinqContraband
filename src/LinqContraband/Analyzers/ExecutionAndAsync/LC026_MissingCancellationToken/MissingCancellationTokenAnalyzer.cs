using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

/// <summary>
/// Analyzes EF Core async calls to ensure CancellationToken is passed when available. Diagnostic ID: LC026
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC026";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Missing CancellationToken in async call";

    private static readonly LocalizableString MessageFormat =
        "The async method '{0}' is called without a CancellationToken. Pass a token to ensure the query can be cancelled.";

    private static readonly LocalizableString Description =
        "Always pass a CancellationToken to EF Core async operations to prevent resource waste when requests are cancelled.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC026_MissingCancellationToken.md");

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

        if (!IsCandidateAsyncEfMethod(method, out var ctParameter))
            return;

        if (!HasUsableCancellationTokenInScope(context.Operation.SemanticModel, invocation.Syntax.SpanStart))
            return;

        var ctArgument = FindCancellationTokenArgument(invocation, ctParameter!);
        if (ctArgument == null || ctArgument.IsImplicit || IsUsingDefault(ctArgument.Value))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }
}
