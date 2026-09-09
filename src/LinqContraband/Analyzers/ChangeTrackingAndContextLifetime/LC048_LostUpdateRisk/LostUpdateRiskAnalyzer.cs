using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC048_LostUpdateRisk;

/// <summary>
/// Reports tracked read-modify-write operations which reach SaveChanges without a proven
/// optimistic-concurrency check.
/// Diagnostic ID: LC048
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LostUpdateRiskAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC048";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Tracked update can overwrite a concurrent change",
        "'{0}' is computed from previously loaded state and can overwrite a concurrent change",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A tracked entity property is read and then written before SaveChanges. Configure an optimistic concurrency token or use an atomic database update to prevent lost updates.",
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC048_LostUpdateRisk.md"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            var evidence = LostUpdateCompilationEvidence.Create(
                startContext.Compilation,
                startContext.CancellationToken
            );

            startContext.RegisterOperationBlockAction(operationContext =>
                LostUpdateFlowAnalysis.Analyze(operationContext, evidence, Rule)
            );
        });
    }
}
