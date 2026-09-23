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
            // An omitted optional token is an implicit argument whose syntax is the call itself, or
            // the ArgumentSyntax around the call when the call is another method's argument.
            // Only a token written in this call's own argument list can be replaced.
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is null ||
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
