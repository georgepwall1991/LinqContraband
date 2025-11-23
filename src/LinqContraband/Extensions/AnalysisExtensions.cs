using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static class AnalysisExtensions
{
    public static bool IsFrameworkMethod(this IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is null || ns.Length == 0) return false;
        return ns.StartsWith("System") || ns.StartsWith("Microsoft");
    }

    public static bool IsIQueryable(this ITypeSymbol? type)
    {
        if (type == null) return false;

        if (IsIQueryableType(type)) return true;

        foreach (var i in type.AllInterfaces)
            if (IsIQueryableType(i))
                return true;
        return false;
    }

    private static bool IsIQueryableType(ITypeSymbol type)
    {
        // Check if namespace is System.Linq and Name is IQueryable
        return type.Name == "IQueryable" && type.ContainingNamespace?.ToString() == "System.Linq";
    }

    public static IOperation UnwrapConversions(this IOperation operation)
    {
        var current = operation;
        while (current is IConversionOperation conversion)
            current = conversion.Operand;
        return current;
    }

    public static bool IsDbContext(this ITypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;

            current = current.BaseType;
        }

        return false;
    }

    public static bool IsDbSet(this ITypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbSet" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;

            current = current.BaseType;
        }

        return false;
    }

    public static bool IsInsideLoop(this IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ILoopOperation) return true;
            current = current.Parent;
        }

        return false;
    }

    public static bool IsInsideAsyncForEach(this IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IForEachLoopOperation forEach && forEach.IsAsynchronous)
                return true;
            current = current.Parent;
        }

        return false;
    }
}
