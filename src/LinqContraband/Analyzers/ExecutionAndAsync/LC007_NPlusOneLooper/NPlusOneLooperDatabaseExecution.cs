using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static partial class NPlusOneLooperAnalysis
{
    public static bool HasStronglyTypedNavigationAccessor(IInvocationOperation loadInvocation)
    {
        if (loadInvocation.GetInvocationReceiver() is not IInvocationOperation accessInvocation)
            return false;

        if (!IsNavigationAccessInvocation(accessInvocation))
            return false;

        return accessInvocation.Arguments.Length == 1 &&
               accessInvocation.Arguments[0].Value is IAnonymousFunctionOperation;
    }

    private static bool TryMatchDatabaseExecution(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        out NPlusOneLoopMatch match)
    {
        var method = invocation.TargetMethod;

        if (method.Name is "Find" or "FindAsync" && method.ContainingType.IsDbSet())
        {
            match = new NPlusOneLoopMatch(
                NPlusOneLooperDiagnosticProperties.Find,
                method.Name,
                string.Empty,
                false);
            return true;
        }

        if (method.Name is "Load" or "LoadAsync" && IsExplicitLoadReceiver(invocation.GetInvocationReceiverType()))
        {
            match = new NPlusOneLoopMatch(
                NPlusOneLooperDiagnosticProperties.ExplicitLoad,
                method.Name,
                string.Empty,
                HasStronglyTypedNavigationAccessor(invocation));
            return true;
        }

        if (!ImmediateQueryExecutionMethods.Contains(method.Name) && !SetBasedExecutorMethods.Contains(method.Name))
        {
            match = null!;
            return false;
        }

        var provenance = AnalyzeQueryProvenance(invocation.GetInvocationReceiver(), invocation, cancellationToken);
        if (provenance.Kind != QueryProvenanceKind.Proven)
        {
            match = null!;
            return false;
        }

        match = new NPlusOneLoopMatch(
            SetBasedExecutorMethods.Contains(method.Name)
                ? NPlusOneLooperDiagnosticProperties.EfSetBasedExecutor
                : provenance.IsNavigationQuery
                    ? NPlusOneLooperDiagnosticProperties.NavigationQueryMaterializer
                    : NPlusOneLooperDiagnosticProperties.EfQueryMaterializer,
            method.Name,
            string.Empty,
            false);
        return true;
    }
}
