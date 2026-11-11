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
        if (match != null)
            ReportHelperCall(context, invocation.Syntax, match);
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context, NPlusOneLooperHelperCache helperCache)
    {
        var methodReference = (IMethodReferenceOperation)context.Operation;
        var match = NPlusOneLooperAnalysis.AnalyzeHelperMethodGroup(methodReference, helperCache, context.CancellationToken);
        if (match != null)
            ReportHelperCall(context, methodReference.Syntax, match);
    }

    private static void ReportHelperCall(OperationAnalysisContext context, SyntaxNode syntax, NPlusOneHelperCallMatch match)
    {
        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties[NPlusOneLooperDiagnosticProperties.PatternKind] = NPlusOneLooperDiagnosticProperties.HelperCall;
        properties[NPlusOneLooperDiagnosticProperties.MethodName] = match.QueryMethodName;
        properties[NPlusOneLooperDiagnosticProperties.HelperName] = match.HelperName;
        properties[NPlusOneLooperDiagnosticProperties.LoopKind] = match.LoopKind;
        properties[NPlusOneLooperDiagnosticProperties.FixerEligible] = "false";

        context.ReportDiagnostic(
            match.OperatorName == null
                ? Diagnostic.Create(
                    HelperCallRule,
                    syntax.GetLocation(),
                    properties.ToImmutable(),
                    match.HelperName,
                    match.QueryMethodName)
                : Diagnostic.Create(
                    PerElementLambdaHelperCallRule,
                    syntax.GetLocation(),
                    properties.ToImmutable(),
                    match.HelperName,
                    match.QueryMethodName,
                    match.OperatorName));
    }
}
