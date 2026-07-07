using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    private static bool TryGetInputSequenceArgument(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken,
        out ArgumentSyntax sequenceArgument)
    {
        if (semanticModel != null)
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (semanticModel.GetOperation(argument, cancellationToken) is IArgumentOperation argumentOperation &&
                    argumentOperation.Parameter?.Type.IsIQueryable() == true)
                {
                    sequenceArgument = argument;
                    return true;
                }
            }
        }

        return TryGetNamedSequenceArgument(invocation.ArgumentList, out sequenceArgument);
    }

    private static bool TryGetNamedSequenceArgument(
        ArgumentListSyntax argumentList,
        out ArgumentSyntax sequenceArgument)
    {
        if (argumentList.Arguments.Count == 0)
        {
            sequenceArgument = null!;
            return false;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText is "source" or "outer")
            {
                sequenceArgument = argument;
                return true;
            }
        }

        sequenceArgument = argumentList.Arguments[0];
        return true;
    }
}
