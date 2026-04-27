using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Analyzes database execution inside loops, causing N+1 query problems. Diagnostic ID: LC007
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Executing database work once per loop iteration multiplies latency, load, and query cost.
/// This includes direct lookups, explicit loading, query materialization, and EF set-based executors when they run inside
/// a loop body. The rule intentionally prefers proof over guesswork: it reports only when EF-backed execution and
/// per-iteration execution are both provable.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NPlusOneLooperAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC007";
    private const string Category = "Performance";
    private const string HelpLinkUri = "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC007_NPlusOneLooper.md";

    private static readonly LocalizableString Title = "N+1 Problem: Database execution inside loop";

    private static readonly LocalizableString MessageFormat =
        "Executing '{0}' inside a loop causes N+1 database operations. Fetch data in bulk or eager load before the loop.";

    private static readonly LocalizableString Description =
        "Running EF Core database execution inside a loop causes one database operation per iteration. This includes Find/FindAsync, explicit loading, query materializers, aggregates, and EF set-based executors when the query source is provably EF-backed.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: HelpLinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var match = NPlusOneLooperAnalysis.AnalyzeInvocation(invocation, context.CancellationToken);
        if (match == null)
            return;

        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties[NPlusOneLooperDiagnosticProperties.PatternKind] = match.PatternKind;
        properties[NPlusOneLooperDiagnosticProperties.MethodName] = match.MethodName;
        properties[NPlusOneLooperDiagnosticProperties.LoopKind] = match.LoopKind;
        properties[NPlusOneLooperDiagnosticProperties.FixerEligible] = match.FixerEligible ? "true" : "false";

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                properties.ToImmutable(),
                match.MethodName));
    }
}
