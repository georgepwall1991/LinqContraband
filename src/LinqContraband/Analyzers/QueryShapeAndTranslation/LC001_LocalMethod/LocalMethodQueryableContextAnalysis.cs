using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodAnalyzer
{
    private static bool IsInsideQueryableLambda(IInvocationOperation invocation)
    {
        var lambda = FindEnclosingLambda(invocation);
        if (lambda == null)
            return false;

        var current = lambda.Parent;
        while (current != null)
        {
            if (current is IInvocationOperation queryInvocation)
            {
                var type = queryInvocation.Instance?.Type;
                if (type == null && queryInvocation.Arguments.Length > 0)
                    type = queryInvocation.Arguments[0].Value.Type;

                if (type.IsIQueryable())
                    return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static IOperation? FindEnclosingLambda(IOperation operation)
    {
        var parent = operation.Parent;
        while (parent != null)
        {
            if (parent.Kind == OperationKind.AnonymousFunction)
                return parent;

            parent = parent.Parent;
        }

        return null;
    }
}
