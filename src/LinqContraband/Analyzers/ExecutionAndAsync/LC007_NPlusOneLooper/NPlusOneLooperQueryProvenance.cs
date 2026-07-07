using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static partial class NPlusOneLooperAnalysis
{
    private static QueryProvenance AnalyzeQueryProvenance(
        IOperation? operation,
        IOperation? analysisScope,
        CancellationToken cancellationToken)
    {
        if (operation == null || analysisScope == null)
            return QueryProvenance.None;

        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var current = operation.UnwrapConversions();

        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (IsClientBoundaryInvocation(invocation))
                        return QueryProvenance.None;

                    if (IsDbContextSetInvocation(invocation))
                        return QueryProvenance.Proven;

                    if (IsNavigationQueryInvocation(invocation))
                        return QueryProvenance.NavigationQuery;

                    if (IsAsQueryableInvocation(invocation))
                        return QueryProvenance.Ambiguous;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    if (propertyReference.Type.IsDbSet())
                        return QueryProvenance.Proven;

                    if (propertyReference.Type.IsIQueryable())
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;

                case IFieldReferenceOperation fieldReference:
                    if (fieldReference.Type.IsDbSet())
                        return QueryProvenance.Proven;

                    if (fieldReference.Type.IsIQueryable())
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;

                case ILocalReferenceOperation localReference:
                    if (!visitedLocals.Add(localReference.Local))
                        return QueryProvenance.Ambiguous;

                    if (!TryGetSingleAssignedLocalValue(localReference.Local, analysisScope, cancellationToken, out var valueOperation))
                    {
                        if (localReference.Type.IsDbSet() || localReference.Type.IsIQueryable())
                            return QueryProvenance.Ambiguous;

                        return QueryProvenance.None;
                    }

                    current = valueOperation;
                    continue;

                case IParameterReferenceOperation parameterReference:
                    if (parameterReference.Type.IsDbSet() ||
                        parameterReference.Type.IsIQueryable() ||
                        parameterReference.Type.IsDbContext())
                    {
                        return QueryProvenance.Ambiguous;
                    }

                    return QueryProvenance.None;

                default:
                    if (current.Type?.IsDbSet() == true)
                        return QueryProvenance.Proven;

                    if (current.Type?.IsIQueryable() == true)
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;
            }
        }

        return QueryProvenance.None;
    }

    private enum QueryProvenanceKind
    {
        None,
        Proven,
        Ambiguous
    }

    private readonly struct QueryProvenance
    {
        public static QueryProvenance None => new(QueryProvenanceKind.None, false);
        public static QueryProvenance Proven => new(QueryProvenanceKind.Proven, false);
        public static QueryProvenance NavigationQuery => new(QueryProvenanceKind.Proven, true);
        public static QueryProvenance Ambiguous => new(QueryProvenanceKind.Ambiguous, false);

        public QueryProvenance(QueryProvenanceKind kind, bool isNavigationQuery)
        {
            Kind = kind;
            IsNavigationQuery = isNavigationQuery;
        }

        public QueryProvenanceKind Kind { get; }
        public bool IsNavigationQuery { get; }
    }
}
