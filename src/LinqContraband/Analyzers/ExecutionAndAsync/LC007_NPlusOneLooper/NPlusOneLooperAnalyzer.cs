using System.Collections.Immutable;
using LinqContraband.Catalog;
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
public sealed partial class NPlusOneLooperAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC007";
    private const string Category = "Performance";
    private const string HelpLinkUri = RuleCatalog.DocumentationSiteUri + "LC007_NPlusOneLooper.html";

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

    private static readonly LocalizableString HelperCallMessageFormat =
        "'{0}' runs '{1}' on every iteration of the loop, causing N+1 database operations. Fetch data in bulk or eager load before the loop.";

    /// <summary>
    /// LC007 reported on a loop's call to a helper method that runs the query. The host accepts any diagnostic whose
    /// ID a supported descriptor declares, so this variant message shares LC007's ID and stays out of
    /// <see cref="SupportedDiagnostics"/>: tests and tools keep resolving LC007 to a single descriptor.
    /// </summary>
    public static readonly DiagnosticDescriptor HelperCallRule = new(
        DiagnosticId,
        Title,
        HelperCallMessageFormat,
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
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var helperCache = new NPlusOneLooperHelperCache(compilationContext.Compilation, NPlusOneLooperAnalysis.MaxHelperDepth);
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, helperCache),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, NPlusOneLooperHelperCache helperCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var match = NPlusOneLooperAnalysis.AnalyzeInvocation(invocation, context.CancellationToken);
        if (match == null)
        {
            ReportHelperCall(context, invocation, helperCache);
            return;
        }

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
