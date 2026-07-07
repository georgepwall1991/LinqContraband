using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

public sealed partial class MissingCancellationTokenFixer
{
    private static ArgumentSyntax? FindExplicitCancellationTokenArgument(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return null;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is null ||
                !IsCancellationTokenParameter(argument.Parameter) ||
                argument.Syntax is not ArgumentSyntax syntax)
            {
                continue;
            }

            return syntax;
        }

        return null;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }
}
