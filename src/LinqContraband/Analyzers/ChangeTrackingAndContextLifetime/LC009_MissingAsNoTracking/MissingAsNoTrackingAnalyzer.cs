using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Analyzes Entity Framework Core queries to detect missing AsNoTracking() calls in read-only operations. Diagnostic ID: LC009
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When querying entities for read-only operations, EF Core creates change tracking snapshots
/// by default, which consumes memory and CPU time. Using AsNoTracking() prevents unnecessary tracking overhead and improves
/// performance in scenarios where entities are not being modified.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC009";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Performance: Missing AsNoTracking() in Read-Only path";

    private static readonly LocalizableString MessageFormat =
        "Method '{0}' appears to be read-only but returns tracked entities. Use AsNoTracking() to avoid tracking overhead.";

    private static readonly LocalizableString Description =
        "When querying entities for read-only operations, use .AsNoTracking() to prevent EF Core from creating unnecessary change tracking snapshots. This reduces memory usage and CPU time.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC009_MissingAsNoTracking.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var writeOperationCache = new ConcurrentDictionary<SyntaxNode, bool>();
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, writeOperationCache),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentDictionary<SyntaxNode, bool> writeOperationCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsEntityMaterializer(method))
            return;

        var enclosingSymbol = context.Operation.SemanticModel?.GetEnclosingSymbol(invocation.Syntax.SpanStart);
        if (enclosingSymbol is IMethodSymbol enclosingMethod && enclosingMethod.ReturnType.IsIQueryable())
            return;

        var analysis = AnalyzeQueryChain(invocation);
        if (!analysis.IsEfQuery || analysis.IsAmbiguousSource)
            return;
        if (analysis.HasAsNoTracking || analysis.HasAsTracking || analysis.HasSelect)
            return;

        if (HasWriteOperations(context.Operation, writeOperationCache))
            return;

        // A property mutation of the materialized entity marks this as a write path even
        // when the SaveChanges lives in a helper the analyzer cannot see — suggesting
        // AsNoTracking would break that cross-method save.
        if (MaterializedEntityIsMutated(invocation, context.CancellationToken))
            return;

        var containingMethodName = GetContainingMethodName(context.Operation);
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), containingMethodName));
    }

    private static string GetContainingMethodName(IOperation operation)
    {
        var sym = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        return sym?.Name ?? "Unknown";
    }

    private sealed class ChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool IsAmbiguousSource { get; set; }
        public bool HasAsNoTracking { get; set; }
        public bool HasAsTracking { get; set; }
        public bool HasSelect { get; set; }
    }
}
