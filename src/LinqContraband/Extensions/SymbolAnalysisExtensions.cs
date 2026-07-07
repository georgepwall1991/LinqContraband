using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static bool IsFrameworkMethod(this IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is null || ns.Length == 0) return false;

        return ns.Equals("System", StringComparison.Ordinal) ||
               ns.StartsWith("System.", StringComparison.Ordinal) ||
               ns.Equals("Microsoft", StringComparison.Ordinal) ||
               ns.StartsWith("Microsoft.", StringComparison.Ordinal);
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
        return type.Name == "IQueryable" && type.ContainingNamespace?.ToString() == "System.Linq";
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

    public static bool IsMaterializerMethod(this string methodName)
    {
        return methodName is
            "ToList" or "ToListAsync" or
            "ToArray" or "ToArrayAsync" or
            "ToDictionary" or "ToDictionaryAsync" or
            "ToHashSet" or "ToHashSetAsync" or
            "AsEnumerable" or
            "First" or "FirstOrDefault" or
            "FirstAsync" or "FirstOrDefaultAsync" or
            "Single" or "SingleOrDefault" or
            "SingleAsync" or "SingleOrDefaultAsync" or
            "Last" or "LastOrDefault" or
            "LastAsync" or "LastOrDefaultAsync" or
            "Count" or "LongCount" or
            "CountAsync" or "LongCountAsync" or
            "Any" or "All" or
            "AnyAsync" or "AllAsync" or
            "Sum" or "Average" or "Min" or "Max" or
            "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync" or
            "Load" or "LoadAsync" or
            "ForEachAsync" or
            "ExecuteDelete" or "ExecuteDeleteAsync" or
            "ExecuteUpdate" or "ExecuteUpdateAsync";
    }

}
