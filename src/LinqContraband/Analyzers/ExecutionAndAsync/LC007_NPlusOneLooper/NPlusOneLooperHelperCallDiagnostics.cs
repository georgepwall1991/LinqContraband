using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

public sealed partial class NPlusOneLooperAnalyzer
{
    /// <summary>
    /// Reports a loop's call to a same-compilation helper that runs the query. The code fix does not apply: the
    /// helper call is not an explicit load it can turn into <c>Include</c>.
    /// </summary>
    private static void ReportHelperCall(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache helperCache)
    {
        var match = NPlusOneLooperAnalysis.AnalyzeHelperCall(invocation, helperCache, context.CancellationToken);
        if (match == null)
            return;

        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties[NPlusOneLooperDiagnosticProperties.PatternKind] = NPlusOneLooperDiagnosticProperties.HelperCall;
        properties[NPlusOneLooperDiagnosticProperties.MethodName] = match.QueryMethodName;
        properties[NPlusOneLooperDiagnosticProperties.HelperName] = match.HelperName;
        properties[NPlusOneLooperDiagnosticProperties.LoopKind] = match.LoopKind;
        properties[NPlusOneLooperDiagnosticProperties.FixerEligible] = "false";

        context.ReportDiagnostic(
            Diagnostic.Create(
                HelperCallRule,
                invocation.Syntax.GetLocation(),
                properties.ToImmutable(),
                match.HelperName,
                match.QueryMethodName));
    }
}
