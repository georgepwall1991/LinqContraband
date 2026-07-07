using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static class NPlusOneLooperDiagnosticProperties
{
    public const string PatternKind = "LC007.PatternKind";
    public const string MethodName = "LC007.MethodName";
    public const string LoopKind = "LC007.LoopKind";
    public const string FixerEligible = "LC007.FixerEligible";

    public const string Find = "Find";
    public const string ExplicitLoad = "ExplicitLoad";
    public const string NavigationQueryMaterializer = "NavigationQueryMaterializer";
    public const string EfQueryMaterializer = "EfQueryMaterializer";
    public const string EfSetBasedExecutor = "EfSetBasedExecutor";
}

internal sealed class NPlusOneLoopMatch
{
    public NPlusOneLoopMatch(string patternKind, string methodName, string loopKind, bool fixerEligible)
    {
        PatternKind = patternKind;
        MethodName = methodName;
        LoopKind = loopKind;
        FixerEligible = fixerEligible;
    }

    public string PatternKind { get; }
    public string MethodName { get; }
    public string LoopKind { get; }
    public bool FixerEligible { get; }
}

internal static partial class NPlusOneLooperAnalysis
{
    public static NPlusOneLoopMatch? AnalyzeInvocation(IInvocationOperation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loop = FindPerIterationLoop(invocation);
        if (loop == null)
            return null;

        if (!TryMatchDatabaseExecution(invocation, cancellationToken, out var match))
            return null;

        return new NPlusOneLoopMatch(match.PatternKind, match.MethodName, loop.GetLoopKind(), match.FixerEligible);
    }

    private static ILoopOperation? FindPerIterationLoop(IInvocationOperation invocation)
    {
        var current = invocation.Parent;
        while (current != null)
        {
            if (current is ILoopOperation loop &&
                invocation.SharesOwningExecutableRoot(loop) &&
                IsPerIterationInvocation(invocation, loop))
            {
                return loop;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsPerIterationInvocation(IInvocationOperation invocation, ILoopOperation loop)
    {
        var spanStart = invocation.Syntax.SpanStart;

        return loop.Syntax switch
        {
            // CommonForEachStatementSyntax covers both the regular `foreach (var x in xs)` and the
            // deconstruction `foreach (var (a, b) in xs)` (ForEachVariableStatementSyntax) shapes.
            CommonForEachStatementSyntax forEach => forEach.Statement.Span.Contains(spanStart),
            ForStatementSyntax forStatement =>
                forStatement.Statement.Span.Contains(spanStart) ||
                (forStatement.Condition?.Span.Contains(spanStart) == true) ||
                forStatement.Incrementors.Any(incrementor => incrementor.Span.Contains(spanStart)),
            WhileStatementSyntax whileStatement =>
                whileStatement.Statement.Span.Contains(spanStart) ||
                whileStatement.Condition.Span.Contains(spanStart),
            DoStatementSyntax doStatement =>
                doStatement.Statement.Span.Contains(spanStart) ||
                doStatement.Condition.Span.Contains(spanStart),
            _ => false
        };
    }

    public static bool IsProvenEfQuerySource(IOperation? operation, CancellationToken cancellationToken = default)
    {
        return AnalyzeQueryProvenance(operation, operation, cancellationToken).Kind == QueryProvenanceKind.Proven;
    }

}
