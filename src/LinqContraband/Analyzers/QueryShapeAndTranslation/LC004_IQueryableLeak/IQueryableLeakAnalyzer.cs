using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

/// <summary>
/// Analyzes IQueryable instances being passed to methods that only accept IEnumerable, causing implicit materialization. Diagnostic ID: LC004
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Passing an IQueryable to an IEnumerable parameter erases query-provider semantics. If the receiving method
/// is proven to enumerate that parameter, the remaining work happens in memory instead of being composed into the provider query. Callers should
/// materialize explicitly to make that cost obvious, or the callee should keep the parameter queryable.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IQueryableLeakAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC004";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Deferred Execution Leak: IQueryable passed as IEnumerable";

    private static readonly LocalizableString MessageFormat =
        "Passing IQueryable to parameter '{0}' of type '{1}' forces in-memory execution. Change the parameter to IQueryable or materialize explicitly.";

    private static readonly LocalizableString Description =
        "Warns only when the target method is inspectable in the current compilation and is proven to enumerate the IEnumerable parameter or forward it into another proven sink.";

    internal static readonly DiagnosticDescriptor Rule = new(
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
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var state = new IQueryableLeakCompilationState(context.Compilation);
        if (!state.CanAnalyze)
            return;

        context.RegisterOperationAction(operationContext => state.AnalyzeInvocation(operationContext), OperationKind.Invocation);
    }
}
