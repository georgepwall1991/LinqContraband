using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A lambda that an in-memory operator runs once per element is a loop written as an expression:
/// <c>ids.Select(id => db.Users.Find(id))</c>, <c>users.ForEach(u => ...)</c>, <c>Parallel.ForEach(ids, ...)</c>.
/// Only delegates count. A lambda passed to an <c>IQueryable</c> operator is an expression tree the provider
/// translates into the single query that contains it, together with every lambda nested inside it. Operators are matched by containing type, so a method that
/// merely shares a name with a LINQ one (an <c>Option.Select</c>, a cache factory) is never a per-element operator.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private static readonly ImmutableHashSet<string> EnumerableOperators = ImmutableHashSet.Create(
        "Select", "SelectMany", "Where", "Any", "All", "Count", "LongCount",
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault",
        "Sum", "Min", "Max", "Average", "MinBy", "MaxBy",
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "ToDictionary", "ToLookup", "DistinctBy", "CountBy", "AggregateBy",
        "TakeWhile", "SkipWhile", "Aggregate", "Zip", "ForAll");

    private static readonly ImmutableHashSet<string> ListAndArrayOperators = ImmutableHashSet.Create(
        "ForEach", "ConvertAll", "Find", "FindAll", "FindIndex", "FindLast", "FindLastIndex",
        "Exists", "TrueForAll", "RemoveAll");

    /// <summary>
    /// The in-memory operator call whose lambda directly contains <paramref name="operation"/> and runs it once per
    /// element, or null. The nearest enclosing lambda decides: a query inside a cache factory inside a
    /// <c>Select</c> lambda belongs to the factory. Operators over <c>Chunk(...)</c> batches run once per batch and
    /// are not returned.
    /// </summary>
    internal static IInvocationOperation? FindPerElementOperator(IOperation operation, out IAnonymousFunctionOperation? lambda)
    {
        lambda = null;
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
            {
                lambda = anonymousFunction;
                break;
            }

            if (current is ILocalFunctionOperation or IMethodBodyBaseOperation)
                return null;
        }

        if (lambda == null)
            return null;

        var passedAs = lambda.Parent;
        while (passedAs is IDelegateCreationOperation or IConversionOperation)
            passedAs = passedAs.Parent;

        if (passedAs is not IArgumentOperation { Parent: IInvocationOperation call, Parameter: { } parameter } ||
            parameter.Type.TypeKind != TypeKind.Delegate ||
            !IsPerElementOperator(call.TargetMethod.ReducedFrom ?? call.TargetMethod, parameter) ||
            IsOverChunkBatches(call) ||
            IsInsideExpressionTree(call))
        {
            return null;
        }

        return call;
    }

    /// <summary>
    /// <c>db.Users.Where(u => ids.Any(id => db.Orders.Any(o => o.UserId == id)))</c>: the inner lambda is a
    /// delegate, but the whole operator call sits in an expression tree the provider translates as one query.
    /// </summary>
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
                if (wrapper.Type is INamedTypeSymbol { Name: "Expression", Arity: 1 } expression &&
                    expression.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPerElementOperator(IMethodSymbol method, IParameterSymbol delegateParameter)
    {
        var type = method.ContainingType;
        if (type == null)
            return false;

        switch (type.ContainingNamespace?.ToDisplayString())
        {
            case "System.Linq" when type.Name is "Enumerable" or "ParallelEnumerable":
                // Aggregate's result selector runs once, on the final accumulator.
                return EnumerableOperators.Contains(method.Name) &&
                       !(method.Name == "Aggregate" && delegateParameter.Name != "func");
            case "System.Collections.Generic" when type.Name == "List":
            case "System" when type.Name == "Array":
                return ListAndArrayOperators.Contains(method.Name);
            case "System.Threading.Tasks" when type.Name == "Parallel":
                return method.Name is "ForEach" or "ForEachAsync" or "For" && delegateParameter.Name == "body";
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>ids.Chunk(500).Select(batch => ...)</c> runs its lambda once per batch, the usual way to keep an
    /// <c>IN</c> list under the provider's parameter limit.
    /// </summary>
    private static bool IsOverChunkBatches(IInvocationOperation operatorCall)
    {
        var source = GetSequenceSource(operatorCall);
        while (source is IInvocationOperation call)
        {
            var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
            if (IsMethod(method, "System.Linq", "Enumerable", "Chunk"))
                return true;

            if (method.Name is not ("ToList" or "ToArray" or "AsEnumerable" or "AsParallel"))
                return false;

            source = GetSequenceSource(call);
        }

        return false;
    }

    private static IOperation? GetSequenceSource(IInvocationOperation call)
    {
        if (call.Instance != null)
            return call.Instance.UnwrapConversions();

        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
                return argument.Value.UnwrapConversions();
        }

        return null;
    }
}
