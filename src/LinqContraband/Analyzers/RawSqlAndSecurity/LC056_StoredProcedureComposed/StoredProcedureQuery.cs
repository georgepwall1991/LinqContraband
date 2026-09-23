using System;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC056_StoredProcedureComposed;

/// <summary>Shared by the LC056 analyzer and fixer.</summary>
internal static class StoredProcedureQuery
{
    private const string EfNamespace = "Microsoft.EntityFrameworkCore";

    private static readonly ImmutableHashSet<string> SetSources =
        ImmutableHashSet.Create(StringComparer.Ordinal, "FromSql", "FromSqlRaw", "FromSqlInterpolated");

    private static readonly ImmutableHashSet<string> DatabaseSources =
        ImmutableHashSet.Create(StringComparer.Ordinal, "SqlQuery", "SqlQueryRaw");

    // EF Core operators that change nothing in the generated SQL.
    private static readonly ImmutableHashSet<string> EfPassThrough = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsTracking", "IgnoreQueryFilters",
        "IgnoreAutoIncludes", "TagWith", "TagWithCallSite", "AsSplitQuery", "AsSingleQuery");

    // EF Core operators that translate into SQL around the source, so they need a composable source.
    private static readonly ImmutableHashSet<string> EfComposing = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Include", "ThenInclude",
        "AllAsync", "AnyAsync", "AverageAsync", "ContainsAsync", "CountAsync", "LongCountAsync",
        "ElementAtAsync", "ElementAtOrDefaultAsync", "FirstAsync", "FirstOrDefaultAsync", "LastAsync",
        "LastOrDefaultAsync", "MaxAsync", "MinAsync", "SingleAsync", "SingleOrDefaultAsync", "SumAsync",
        "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync");

    public static bool IsStoredProcedureCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var containingType = method.ContainingType;
        if (containingType?.ContainingNamespace?.ToDisplayString() != EfNamespace) return false;

        var isSource = containingType.Name switch
        {
            "RelationalQueryableExtensions" => SetSources.Contains(method.Name),
            "RelationalDatabaseFacadeExtensions" => DatabaseSources.Contains(method.Name),
            _ => false
        };
        if (!isSource) return false;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 1)
                return GetLeadingText(argument.Value) is { } text && StartsWithExec(text);
        }

        return false;
    }

    /// <summary>
    /// When <paramref name="invocation"/> is an operator that EF Core must translate around its source, walks back
    /// through operators that leave the SQL unchanged and returns the stored procedure call it composes over.
    /// </summary>
    public static IInvocationOperation? FindComposedStoredProcedureCall(IInvocationOperation invocation)
    {
        if (Classify(invocation) != Step.Composes) return null;

        var current = GetSource(invocation);
        while (current is IInvocationOperation previous)
        {
            if (IsStoredProcedureCall(previous)) return previous;
            if (Classify(previous) != Step.PassThrough) return null;
            current = GetSource(previous);
        }

        return null;
    }

    private static IOperation? GetSource(IInvocationOperation invocation)
    {
        if (!invocation.TargetMethod.IsExtensionMethod) return null;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
                return argument.Value.UnwrapConversions();
        }

        return null;
    }

    private enum Step
    {
        Stop,
        PassThrough,
        Composes
    }

    private static Step Classify(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        switch (method.ContainingType?.ToDisplayString())
        {
            case "System.Linq.Queryable":
                if (method.Name is "AsQueryable" || IsIdentitySelect(invocation)) return Step.PassThrough;
                // Cast<T> to the same type can be a no-op in EF Core, so it is not treated as composition.
                return method.Name == "Cast" ? Step.Stop : Step.Composes;
            case EfNamespace + ".EntityFrameworkQueryableExtensions":
            case EfNamespace + ".RelationalQueryableExtensions":
                if (EfPassThrough.Contains(method.Name)) return Step.PassThrough;
                return EfComposing.Contains(method.Name) ? Step.Composes : Step.Stop;
            default:
                return Step.Stop;
        }
    }

    private static bool IsIdentitySelect(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Select" || invocation.Arguments.Length != 2) return false;

        return invocation.Arguments[1].Value.UnwrapConversions() is IAnonymousFunctionOperation lambda &&
               lambda.Symbol.Parameters.Length == 1 &&
               lambda.Body.Operations.Length == 1 &&
               lambda.Body.Operations[0] is IReturnOperation { ReturnedValue: { } returned } &&
               returned.UnwrapConversions() is IParameterReferenceOperation parameter &&
               SymbolEqualityComparer.Default.Equals(parameter.Parameter, lambda.Symbol.Parameters[0]);
    }

    /// <summary>The SQL text up to the first interpolation hole, when it is known at compile time.</summary>
    private static string? GetLeadingText(IOperation value)
    {
        value = value.UnwrapConversions();
        if (value.ConstantValue is { HasValue: true, Value: string constant }) return constant;

        if (value is IInterpolatedStringOperation interpolated &&
            interpolated.Parts.Length > 0 &&
            interpolated.Parts[0] is IInterpolatedStringTextOperation text &&
            text.Text.ConstantValue is { HasValue: true, Value: string leading })
        {
            return leading;
        }

        return null;
    }

    /// <summary>Skips whitespace and SQL comments the way EF Core does, then looks for EXEC or EXECUTE.</summary>
    public static bool StartsWithExec(string sql)
    {
        var index = 0;
        while (true)
        {
            while (index < sql.Length && char.IsWhiteSpace(sql[index])) index++;

            if (string.CompareOrdinal(sql, index, "--", 0, 2) == 0)
            {
                var lineEnd = sql.IndexOf('\n', index);
                if (lineEnd < 0) return false;
                index = lineEnd + 1;
                continue;
            }

            if (string.CompareOrdinal(sql, index, "/*", 0, 2) == 0)
            {
                var commentEnd = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0) return false;
                index = commentEnd + 2;
                continue;
            }

            break;
        }

        foreach (var keyword in new[] { "EXECUTE", "EXEC" })
        {
            if (string.Compare(sql, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                (index + keyword.Length == sql.Length || char.IsWhiteSpace(sql[index + keyword.Length])))
            {
                return true;
            }
        }

        return false;
    }
}
