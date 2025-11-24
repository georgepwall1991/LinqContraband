using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// Provides extension methods for Roslyn code analysis operations used across LinqContraband analyzers.
/// </summary>
public static class AnalysisExtensions
{
    /// <summary>
    /// Determines whether the method belongs to a framework namespace (System.* or Microsoft.*).
    /// </summary>
    /// <param name="method">The method symbol to check.</param>
    /// <returns><c>true</c> if the method is from a System or Microsoft namespace; otherwise, <c>false</c>.</returns>
    public static bool IsFrameworkMethod(this IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is null || ns.Length == 0) return false;

        // Use proper ordinal comparison to avoid matching namespaces like "SystemX" or "MicrosoftY"
        return ns.Equals("System", StringComparison.Ordinal) ||
               ns.StartsWith("System.", StringComparison.Ordinal) ||
               ns.Equals("Microsoft", StringComparison.Ordinal) ||
               ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the type implements <see cref="System.Linq.IQueryable{T}"/> or <see cref="System.Linq.IQueryable"/>.
    /// </summary>
    /// <param name="type">The type symbol to check. Can be null.</param>
    /// <returns><c>true</c> if the type implements IQueryable; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Unwraps conversion and parenthesized operations to get the underlying operand.
    /// </summary>
    /// <param name="operation">The operation to unwrap.</param>
    /// <returns>The innermost non-conversion, non-parenthesized operation.</returns>
    public static IOperation UnwrapConversions(this IOperation operation)
    {
        var current = operation;
        while (true)
        {
            if (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            break;
        }

        return current;
    }

    /// <summary>
    /// Determines whether the type is or inherits from Entity Framework Core's DbContext.
    /// </summary>
    /// <param name="type">The type symbol to check. Can be null.</param>
    /// <returns><c>true</c> if the type is a DbContext; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Determines whether the type is or inherits from Entity Framework Core's DbSet.
    /// </summary>
    /// <param name="type">The type symbol to check. Can be null.</param>
    /// <returns><c>true</c> if the type is a DbSet; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Determines whether the operation is nested inside a loop construct (for, foreach, while, do-while).
    /// </summary>
    /// <param name="operation">The operation to check.</param>
    /// <returns><c>true</c> if the operation is inside a loop; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Determines whether the operation is nested inside an async foreach (await foreach) loop.
    /// </summary>
    /// <param name="operation">The operation to check.</param>
    /// <returns><c>true</c> if the operation is inside an async foreach; otherwise, <c>false</c>.</returns>
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
