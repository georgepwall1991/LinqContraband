using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC014_AvoidStringCaseConversion;

public sealed partial class AvoidStringCaseConversionAnalyzer
{
    private static readonly HashSet<string> TargetLinqMethods = new()
    {
        "Where",
        "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending",
        "Count", "LongCount",
        "Any", "All",
        "First", "FirstOrDefault",
        "Single", "SingleOrDefault",
        "Last", "LastOrDefault",
        "Join", "GroupJoin"
    };

    private static readonly HashSet<string> TargetEfAsyncPredicateMethods = new()
    {
        "CountAsync", "LongCountAsync",
        "AnyAsync", "AllAsync",
        "FirstAsync", "FirstOrDefaultAsync",
        "SingleAsync", "SingleOrDefaultAsync",
        "LastAsync", "LastOrDefaultAsync"
    };

    private static ImmutableArray<IParameterSymbol> GetEnclosingQueryableLambdaParameters(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation lambda)
            {
                var parent = lambda.Parent;
                while (parent is IConversionOperation)
                    parent = parent.Parent;

                if (parent is IArgumentOperation argument &&
                    argument.Parent is IInvocationOperation linqInvocation)
                {
                    var method = linqInvocation.TargetMethod;
                    if (IsTargetQueryableMethod(method) &&
                        IsLambdaScopedToEntityFrameworkSource(argument, linqInvocation))
                    {
                        return lambda.Symbol.Parameters;
                    }
                }
            }

            current = current.Parent;
        }

        return ImmutableArray<IParameterSymbol>.Empty;
    }

    private static bool IsTargetQueryableMethod(IMethodSymbol method)
    {
        if (TargetLinqMethods.Contains(method.Name) &&
            method.ContainingType.Name == "Queryable" &&
            method.ContainingNamespace?.ToString() == "System.Linq")
        {
            return true;
        }

        return TargetEfAsyncPredicateMethods.Contains(method.Name) &&
               method.ContainingType.Name == "EntityFrameworkQueryableExtensions" &&
               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsLambdaScopedToEntityFrameworkSource(
        IArgumentOperation argument,
        IInvocationOperation linqInvocation)
    {
        var methodName = linqInvocation.TargetMethod.Name;
        if (methodName is "Join" or "GroupJoin")
        {
            return argument.Parameter?.Name switch
            {
                "outerKeySelector" => HasEntityFrameworkQuerySource(linqInvocation.GetInvocationReceiver()),
                "innerKeySelector" => TryGetArgumentValue(linqInvocation, "inner", out var inner) &&
                                      HasEntityFrameworkQuerySource(inner),
                _ => false
            };
        }

        return HasEntityFrameworkQuerySource(linqInvocation.GetInvocationReceiver());
    }
}
