using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

public sealed partial class MissingQueryTagsAnalyzer
{
    /// <summary>
    /// Walks the query from the terminal back to its root and adds up the weight of each shape operator. Succeeds only
    /// when the chain is untagged, reaches a <c>DbSet</c> or <c>DbContext.Set&lt;T&gt;()</c>, and passes through
    /// nothing but LINQ and known EF Core query operators.
    /// </summary>
    private static bool TryScoreChain(IOperation receiver, out int score)
    {
        score = 0;
        var current = UnwrapQuery(receiver);

        while (current is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (method.Name is "TagWith" or "TagWithCallSite")
                return false;

            if (method.Name == "Set" && method.IsGenericMethod && method.ContainingType.IsDbContext())
                return true;

            if (!TryGetOperatorWeight(method, out var weight))
                return false;

            score += weight;

            var next = invocation.GetInvocationReceiver();
            if (next == null)
                return false;

            current = UnwrapQuery(next);
        }

        return current.Type.IsDbSet();
    }

    private static IOperation UnwrapQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is ITranslatedQueryOperation translatedQuery)
            current = translatedQuery.Operation.UnwrapConversions();

        return current;
    }

    private static bool TryGetOperatorWeight(IMethodSymbol method, out int weight)
    {
        weight = 0;
        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        if (IsSystemLinqType(containingType, "Queryable"))
        {
            if (ZeroWeightQueryableOperators.Contains(method.Name))
                return true;

            weight = HeavyQueryableOperators.Contains(method.Name) ? 2 : 1;
            return true;
        }

        if (containingType.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
            return false;

        if (IncludeOperators.Contains(method.Name))
        {
            weight = 1;
            return true;
        }

        return QueryOptionOperators.Contains(method.Name);
    }

    private static bool IsTerminal(IMethodSymbol method)
    {
        if (!TerminalMethods.Contains(method.Name))
            return false;

        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        return IsSystemLinqType(containingType, "Queryable") ||
               IsSystemLinqType(containingType, "Enumerable") ||
               (containingType.Name == "EntityFrameworkQueryableExtensions" &&
                containingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore");
    }

    private static bool IsSystemLinqType(INamedTypeSymbol type, string name)
    {
        return type.Name == name && type.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static bool HasLambdaArgument(IInvocationOperation invocation)
    {
        var skip = invocation.Instance == null ? 1 : 0;
        return invocation.Arguments.Skip(skip).Any(argument => IsLambda(argument.Value));
    }

    private static bool IsLambda(IOperation operation)
    {
        var current = operation;
        while (true)
        {
            switch (current)
            {
                case IConversionOperation conversion:
                    current = conversion.Operand;
                    continue;
                case IDelegateCreationOperation delegateCreation:
                    current = delegateCreation.Target;
                    continue;
                case IParenthesizedOperation parenthesized:
                    current = parenthesized.Operand;
                    continue;
                default:
                    return current is IAnonymousFunctionOperation;
            }
        }
    }

    private static bool IsInsideExpressionTree(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is not IAnonymousFunctionOperation)
                continue;

            for (var wrapper = current.Parent;
                 wrapper is IDelegateCreationOperation or IConversionOperation;
                 wrapper = wrapper.Parent)
            {
                if (IsExpressionTreeType(wrapper.Type))
                    return true;
            }
        }

        return false;
    }

    private static bool IsExpressionTreeType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Expression", IsGenericType: true } named &&
               named.ContainingNamespace?.ToString() == "System.Linq.Expressions";
    }
}
