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
    /// Gets the receiver operation for an invocation, handling both instance and extension-method syntax.
    /// </summary>
    /// <param name="invocation">The invocation operation.</param>
    /// <param name="unwrapConversions">Whether to unwrap implicit conversions on the receiver.</param>
    /// <returns>The receiver operation, or null if none.</returns>
    public static IOperation? GetInvocationReceiver(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        var receiver = invocation.Instance ??
                       (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        if (unwrapConversions && receiver != null)
            receiver = receiver.UnwrapConversions();

        return receiver;
    }

    /// <summary>
    /// Gets the receiver type for an invocation, handling both instance and extension-method syntax.
    /// </summary>
    /// <param name="invocation">The invocation operation.</param>
    /// <param name="unwrapConversions">Whether to unwrap implicit conversions on the receiver.</param>
    /// <returns>The receiver type, or null if none.</returns>
    public static ITypeSymbol? GetInvocationReceiverType(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        return invocation.GetInvocationReceiver(unwrapConversions)?.Type;
    }

    /// <summary>
    /// Unwraps conversion, parenthesized, and await operations to get the underlying operand.
    /// </summary>
    /// <param name="operation">The operation to unwrap.</param>
    /// <returns>The innermost non-conversion, non-parenthesized, non-awaited operation.</returns>
    public static IOperation UnwrapConversions(this IOperation operation)
    {
        var current = operation;
        while (current != null)
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

            if (current is IAwaitOperation awaitOp)
            {
                current = awaitOp.Operation;
                continue;
            }

            break;
        }

        return current ?? operation;
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

    /// <summary>
    /// Determines whether the method name is a query materializer that triggers database execution.
    /// </summary>
    /// <param name="methodName">The method name to check.</param>
    /// <returns><c>true</c> if the method is a materializer; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Materializers are methods that execute the query and return results, such as ToList, First, Count, etc.
    /// This includes both synchronous and asynchronous variants.
    /// </remarks>
    public static bool IsMaterializerMethod(this string methodName)
    {
        return methodName is
            // Collection materializers
            "ToList" or "ToListAsync" or
            "ToArray" or "ToArrayAsync" or
            "ToDictionary" or "ToDictionaryAsync" or
            "ToHashSet" or "ToHashSetAsync" or
            "AsEnumerable" or
            // Single element materializers
            "First" or "FirstOrDefault" or
            "FirstAsync" or "FirstOrDefaultAsync" or
            "Single" or "SingleOrDefault" or
            "SingleAsync" or "SingleOrDefaultAsync" or
            "Last" or "LastOrDefault" or
            "LastAsync" or "LastOrDefaultAsync" or
            // Aggregate materializers
            "Count" or "LongCount" or
            "CountAsync" or "LongCountAsync" or
            "Any" or "All" or
            "AnyAsync" or "AllAsync" or
            "Sum" or "Average" or "Min" or "Max" or
            "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync" or
            // Execution materializers
            "Load" or "LoadAsync" or
            "ForEachAsync" or
            // Bulk operation materializers
            "ExecuteDelete" or "ExecuteDeleteAsync" or
            "ExecuteUpdate" or "ExecuteUpdateAsync";
    }

    /// <summary>
    /// Tries to find a primary key property on the given entity type using EF Core conventions and [Key] attribute.
    /// </summary>
    /// <param name="entityType">The entity type symbol.</param>
    /// <returns>The name of the primary key property, or null if not found.</returns>
    public static string? TryFindPrimaryKey(this ITypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                // Check [Key] attribute
                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass == null) continue;
                    if (attr.AttributeClass.Name == "KeyAttribute" ||
                        (attr.AttributeClass.Name == "Key" &&
                         attr.AttributeClass.ContainingNamespace?.ToString().StartsWith("System.ComponentModel.DataAnnotations", StringComparison.Ordinal) == true))
                    {
                        return prop.Name;
                    }
                }

                // Check Convention: Id
                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;

                // Check Convention: {Entity}Id
                if (prop.Name.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase)) return prop.Name;
            }

            current = current.BaseType;
        }

        return null;
    }
}
