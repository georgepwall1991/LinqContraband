using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowAnalyzer
{
    private static IAnonymousFunctionOperation? FindQueryableLambda(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IArgumentOperation { Parent: IInvocationOperation linqInvocation } &&
                IsTargetQueryableInvocation(linqInvocation))
            {
                return FindEnclosingLambda(operation);
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsTargetQueryableInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return TargetLinqMethods.Contains(method.Name) &&
               method.ContainingType.Name == "Queryable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static IAnonymousFunctionOperation? FindEnclosingLambda(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation lambda)
                return lambda;

            current = current.Parent;
        }

        return null;
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation operation)
    {
        yield return operation;

        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in EnumerateOperations(child))
                yield return descendant;
        }
    }
}
