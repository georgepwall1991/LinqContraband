using LinqContraband.Extensions;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodAnalyzer
{
    private static bool IsTranslationCriticalQueryableInvocation(IInvocationOperation invocation)
    {
        // Handle extension syntax (Instance populated) and static syntax (source is a bound argument).
        var type = invocation.Instance?.Type;
        if (type == null)
            type = GetInputSequenceArgument(invocation)?.Value.Type;

        return type.IsIQueryable() && TranslationCriticalQueryMethods.Contains(invocation.TargetMethod.Name);
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
