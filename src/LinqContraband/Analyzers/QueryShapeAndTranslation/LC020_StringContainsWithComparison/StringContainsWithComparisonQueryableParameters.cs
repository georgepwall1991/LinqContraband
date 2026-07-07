using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC020_StringContainsWithComparison;

public sealed partial class StringContainsWithComparisonAnalyzer
{
    private static ImmutableArray<IParameterSymbol> GetQueryableExpressionLambdaParameters(IOperation operation)
    {
        var lambdaPath = ImmutableArray.CreateBuilder<IAnonymousFunctionOperation>();
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
            {
                lambdaPath.Add(anonymousFunction);

                var lambdaParent = anonymousFunction.Parent;
                while (lambdaParent is IConversionOperation or IDelegateCreationOperation)
                {
                    lambdaParent = lambdaParent.Parent;
                }

                if (lambdaParent is IArgumentOperation { Parent: IInvocationOperation parentInvocation } &&
                    IsQueryableInvocation(parentInvocation))
                {
                    return GetQueryDependentLambdaParameters(lambdaPath);
                }
            }

            current = current.Parent;
        }

        return ImmutableArray<IParameterSymbol>.Empty;
    }

    private static ImmutableArray<IParameterSymbol> GetQueryDependentLambdaParameters(
        ImmutableArray<IAnonymousFunctionOperation>.Builder lambdaPath)
    {
        var dependentParameters = ImmutableArray.CreateBuilder<IParameterSymbol>();
        for (var index = lambdaPath.Count - 1; index >= 0; index--)
        {
            var lambda = lambdaPath[index];
            if (index == lambdaPath.Count - 1 || LambdaSourceDependsOnParameters(lambda, dependentParameters))
            {
                dependentParameters.AddRange(lambda.Symbol.Parameters);
            }
        }

        return dependentParameters.ToImmutable();
    }

    private static bool LambdaSourceDependsOnParameters(
        IAnonymousFunctionOperation lambda,
        ImmutableArray<IParameterSymbol>.Builder dependentParameters)
    {
        var lambdaParent = lambda.Parent;
        while (lambdaParent is IConversionOperation or IDelegateCreationOperation)
        {
            lambdaParent = lambdaParent.Parent;
        }

        if (lambdaParent is not IArgumentOperation { Parent: IInvocationOperation parentInvocation })
            return false;

        var receiver = parentInvocation.GetInvocationReceiver();
        return dependentParameters.Any(parameter => ReceiverDependsOnParameter(receiver, parameter));
    }

    private static bool IsQueryableInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.ContainingType.Name != "Queryable" ||
            method.ContainingNamespace?.ToString() != "System.Linq")
        {
            return false;
        }

        return invocation.GetInvocationReceiverType().IsIQueryable();
    }
}
