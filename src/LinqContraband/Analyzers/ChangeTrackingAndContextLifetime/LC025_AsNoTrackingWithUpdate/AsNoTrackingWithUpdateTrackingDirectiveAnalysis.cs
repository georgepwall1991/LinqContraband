using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateAnalyzer
{
    private static bool IsAsNoTrackingQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (IsEfCoreNoTrackingDirective(invocation.TargetMethod)) return true;

            return HasAsNoTrackingInChain(invocation);
        }

        return false;
    }

    private static bool HasAsNoTrackingInChain(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        var outermostSelectChecked = false;
        while (current is IInvocationOperation inv)
        {
            // Only the outermost Select nearest the materializer determines the materialized shape.
            if (inv.TargetMethod.Name == "Select" && !outermostSelectChecked)
            {
                outermostSelectChecked = true;
                if (IsProjectionToConstructedObject(inv))
                    return false;
            }

            if (IsEfCoreNoTrackingDirective(inv.TargetMethod))
                return true;
            if (IsEfCoreAsTracking(inv.TargetMethod))
                return false;

            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }

        return false;
    }

    private static bool IsProjectionToConstructedObject(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Select")
            return false;

        var selector = invocation.Arguments.LastOrDefault()?.Value?.UnwrapConversions();
        var lambda = selector switch
        {
            IDelegateCreationOperation delegateCreation => delegateCreation.Target as IAnonymousFunctionOperation,
            IAnonymousFunctionOperation direct => direct,
            _ => null
        };

        if (lambda == null)
            return false;

        var projected = GetSingleProjectedExpression(lambda);
        return projected is IObjectCreationOperation or IAnonymousObjectCreationOperation;
    }

    private static IOperation? GetSingleProjectedExpression(IAnonymousFunctionOperation lambda)
    {
        foreach (var op in lambda.Body.Operations)
        {
            switch (op)
            {
                case IReturnOperation { ReturnedValue: { } returned }:
                    return returned.UnwrapConversions();
                case IExpressionStatementOperation expressionStatement:
                    return expressionStatement.Operation.UnwrapConversions();
            }
        }

        return null;
    }

    private static bool IsEfCoreNoTrackingDirective(IMethodSymbol method)
    {
        if (method.Name is not ("AsNoTracking" or "AsNoTrackingWithIdentityResolution"))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsEfCoreAsTracking(IMethodSymbol method)
    {
        if (method.Name != "AsTracking")
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }
}
