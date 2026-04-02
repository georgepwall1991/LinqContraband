using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Detects tracked bulk-update loops that can likely be replaced with ExecuteUpdate/ExecuteUpdateAsync. Diagnostic ID: LC032
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC032";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Use ExecuteUpdate for provable bulk scalar updates";

    private static readonly LocalizableString MessageFormat =
        "Loop updates tracked '{0}' entities and then calls '{1}'. Consider ExecuteUpdate/ExecuteUpdateAsync for a set-based update. Warning: ExecuteUpdate bypasses change tracking and entity callbacks.";

    private static readonly LocalizableString Description =
        "Reports only when a foreach loop over a provable EF query performs direct scalar assignments on tracked entities and is immediately followed by SaveChanges/SaveChangesAsync on the same local DbContext.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC032_ExecuteUpdateForBulkUpdates.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        if (!HasExecuteUpdateSupport(context.Compilation))
            return;

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name is not ("SaveChanges" or "SaveChangesAsync"))
            return;

        if (!method.ContainingType.IsDbContext())
            return;

        if (invocation.Instance?.UnwrapConversions() is not ILocalReferenceOperation dbContextReference)
            return;

        if (!TryGetImmediatelyPreviousForEachLoop(invocation, out var loop))
            return;

        if (!invocation.SharesOwningExecutableRoot(loop))
            return;

        if (!TryAnalyzeLoop(loop, dbContextReference.Local, out var entityTypeName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                loop.Syntax.GetLocation(),
                entityTypeName,
                method.Name));
    }
}
