using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private bool HasPaginationUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is IInvocationOperation inv)
        {
            if (PaginationMethods.Contains(inv.TargetMethod.Name))
                return true;

            var next = inv.GetInvocationReceiver();
            if (next == null)
                break;

            current = next.UnwrapConversions();
        }

        return false;
    }

    private bool HasOrderByUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        while (current != null)
        {
            if (current is IInvocationOperation inv)
            {
                var method = inv.TargetMethod;
                if (SortingMethods.Contains(method.Name) && method.ReturnType.IsIQueryable())
                    return true;

                var next = inv.GetInvocationReceiver();
                if (next == null)
                    return false;

                current = next.UnwrapConversions();
                continue;
            }

            if (current.Type != null && IsOrderedQueryable(current.Type))
                return true;
            return false;
        }

        return false;
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
