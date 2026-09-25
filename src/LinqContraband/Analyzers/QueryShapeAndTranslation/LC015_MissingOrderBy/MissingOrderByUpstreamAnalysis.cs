using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private bool HasPaginationUpstream(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        return FindPaginationUpstream(operation, localValueCache, cancellationToken) != null;
    }

    // A sort after Skip/Take only sorts an arbitrary subset when that window was picked from an
    // unordered set. `OrderByDescending(x => x.Id).Take(10).OrderBy(x => x.Id)` ("latest 10, shown
    // oldest first") re-sorts a deterministic window, so the nearest upstream Skip/Take must itself
    // sit on an unordered source for the misplaced-sort report.
    private bool HasUnorderedPaginationUpstream(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var pagination = FindPaginationUpstream(operation, localValueCache, cancellationToken);
        if (pagination == null)
            return false;

        var paginationReceiver = pagination.GetInvocationReceiver();
        return paginationReceiver == null ||
               !HasOrderByUpstream(paginationReceiver, localValueCache, cancellationToken);
    }

    private IInvocationOperation? FindPaginationUpstream(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var current = operation.UnwrapConversions();
        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            current = current.UnwrapConversions();

            if (current is IInvocationOperation inv)
            {
                if (PaginationMethods.Contains(inv.TargetMethod.Name))
                    return inv;

                var next = inv.GetInvocationReceiver();
                if (next == null)
                    break;

                current = next;
                continue;
            }

            if (current is ILocalReferenceOperation localReference &&
                TryResolveLocalValue(
                    localReference.Local,
                    localReference,
                    localReference.FindOwningExecutableRoot(),
                    localValueCache,
                    cancellationToken,
                    out var resolvedValue))
            {
                current = resolvedValue;
                continue;
            }

            break;
        }

        return null;
    }

    private bool HasOrderByUpstream(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var current = operation.UnwrapConversions();

        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            current = current.UnwrapConversions();

            if (current is IInvocationOperation inv)
            {
                var method = inv.TargetMethod;
                if (SortingMethods.Contains(method.Name) && method.ReturnType.IsIQueryable())
                    return true;

                if (inv.Type != null && IsOrderedQueryable(inv.Type))
                    return true;

                if (IsProjectOrderingHelper(method, cancellationToken))
                    return true;

                var next = inv.GetInvocationReceiver();
                if (next == null)
                    return false;

                current = next.UnwrapConversions();
                continue;
            }

            if (current is ILocalReferenceOperation localReference &&
                TryResolveLocalValue(
                    localReference.Local,
                    localReference,
                    localReference.FindOwningExecutableRoot(),
                    localValueCache,
                    cancellationToken,
                    out var resolvedValue))
            {
                current = resolvedValue;
                continue;
            }

            if (current.Type != null && IsOrderedQueryable(current.Type))
                return true;
            return false;
        }

        return false;
    }

    // A project's own query helper, such as `q = ApplySort(q, sortBy, direction)`, often applies
    // the ordering the caller asked for. Prepending `OrderBy(x => x.Id)` after it would replace that
    // sort, so a helper whose body calls an ordering operator counts as ordered. A helper from
    // another assembly counts when its name says it sorts (`ApplySorting`, `ApplyOrdering`).
    private static bool IsProjectOrderingHelper(IMethodSymbol method, CancellationToken cancellationToken)
    {
        method = method.ReducedFrom ?? method;
        if (!method.ReturnType.IsIQueryable() || IsFrameworkQueryMethod(method))
            return false;

        if (method.DeclaringSyntaxReferences.Length == 0)
            return method.Name.IndexOf("Sort", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   method.Name.IndexOf("Order", System.StringComparison.OrdinalIgnoreCase) >= 0;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            foreach (var node in reference.GetSyntax(cancellationToken).DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax invocation)
                    continue;

                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
                    _ => null
                };

                if (name != null && SortingMethods.Contains(name))
                    return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkQueryMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns != null &&
               (ns == "System.Linq" || ns.StartsWith("System.", System.StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal));
    }

    private bool IsOrderedQueryable(ITypeSymbol type)
    {
        if (type.Name == "IOrderedQueryable" && type.ContainingNamespace?.ToString() == "System.Linq")
            return true;

        foreach (var i in type.AllInterfaces)
        {
            if (i.Name == "IOrderedQueryable" && i.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        }

        return false;
    }
}
