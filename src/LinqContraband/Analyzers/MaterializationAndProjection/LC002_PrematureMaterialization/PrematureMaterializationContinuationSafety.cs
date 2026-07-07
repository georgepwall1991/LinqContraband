using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool HasProviderSafeContinuationArguments(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type is not INamedTypeSymbol parameterType ||
                !IsDelegateLikeParameter(parameterType))
            {
                continue;
            }

            var value = argument.Value.UnwrapConversions();
            if (value is IDelegateCreationOperation delegateCreation)
                value = delegateCreation.Target.UnwrapConversions();

            if (value is not IAnonymousFunctionOperation anonymousFunction ||
                !IsProviderSafeLambdaBody(anonymousFunction.Body))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDelegateLikeParameter(INamedTypeSymbol parameterType)
    {
        return parameterType.TypeKind == TypeKind.Delegate ||
               parameterType.DelegateInvokeMethod != null ||
               (parameterType.Name == "Expression" &&
                parameterType.ContainingNamespace?.ToString() == "System.Linq.Expressions");
    }

    private static bool IsProviderSafeLambdaBody(IOperation body)
    {
        var unwrapped = body.UnwrapConversions();

        if (unwrapped is IBlockOperation block)
        {
            return block.Operations.Length == 1 &&
                   block.Operations[0] is IReturnOperation blockReturn &&
                   blockReturn.ReturnedValue != null &&
                   IsProviderSafeExpression(blockReturn.ReturnedValue);
        }

        if (unwrapped is IReturnOperation returnOperation)
        {
            return returnOperation.ReturnedValue != null &&
                   IsProviderSafeExpression(returnOperation.ReturnedValue);
        }

        return IsProviderSafeExpression(unwrapped);
    }
}
