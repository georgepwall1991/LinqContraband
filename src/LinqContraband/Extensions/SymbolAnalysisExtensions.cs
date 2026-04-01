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

    public static string? TryFindPrimaryKey(this ITypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass == null) continue;
                    if (attr.AttributeClass.Name == "KeyAttribute" ||
                        (attr.AttributeClass.Name == "Key" &&
                         attr.AttributeClass.ContainingNamespace?.ToString()?.StartsWith("System.ComponentModel.DataAnnotations", StringComparison.Ordinal) == true))
                    {
                        return prop.Name;
                    }
                }

                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;
                if (prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;
            }

            current = current.BaseType;
        }

        return null;
    }
}
