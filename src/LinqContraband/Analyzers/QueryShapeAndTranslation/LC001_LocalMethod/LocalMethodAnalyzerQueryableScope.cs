using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodAnalyzer
{
    private static bool IsTranslationCriticalQueryableInvocation(IInvocationOperation invocation)
    {
        // Handle extension syntax (Instance populated) and static syntax (source is a bound argument).
        var source = invocation.Instance ?? GetInputSequenceArgument(invocation)?.Value;

        // list.AsQueryable() runs on LINQ to Objects, where any method can be called.
        return source?.Type.IsIQueryable() == true &&
               TranslationCriticalQueryMethods.Contains(invocation.TargetMethod.Name) &&
               !source.IsProvablyInMemoryQueryable();
    }

    private static bool IsExpressionTreeLambda(IAnonymousFunctionOperation lambda)
    {
        for (var wrapper = lambda.Parent;
             wrapper is IDelegateCreationOperation or IConversionOperation;
             wrapper = wrapper.Parent)
        {
            if (wrapper.Type is INamedTypeSymbol { Name: "Expression", Arity: 1 } expression &&
                expression.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
                return true;
        }

        return false;
    }

    private static IArgumentOperation? GetInputSequenceArgument(IInvocationOperation invocation)
    {
        IArgumentOperation? firstArgument = null;
        IArgumentOperation? namedSequenceArgument = null;

        foreach (var argument in invocation.Arguments)
        {
            firstArgument ??= argument;

            if (argument.Parameter?.Type.IsIQueryable() == true)
                return argument;

            if (argument.Parameter?.Name is "source" or "outer")
                namedSequenceArgument ??= argument;
        }

        return namedSequenceArgument ?? firstArgument;
    }

    private static bool InvocationDependsOnLambdaParameter(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda)
    {
        foreach (var parameter in lambda.Symbol.Parameters)
        {
            if (invocation.ReferencesParameter(parameter))
                return true;
        }

        return false;
    }
}
