using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public sealed partial class FindInsteadOfFirstOrDefaultFixer
{
    private static bool TryCreateFixContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out FixContext fixContext)
    {
        fixContext = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName is not ("FirstOrDefault" or "SingleOrDefault" or "FirstOrDefaultAsync" or "SingleOrDefaultAsync"))
            return false;

        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;

        if (!operation.GetInvocationReceiverType().IsDbSet())
            return false;

        var isAsync = methodName.EndsWith("Async");
        if (isAsync && !IsAwaited(invocation))
            return false;

        var predicateArgument = operation.Arguments
            .FirstOrDefault(argument => argument.Value.UnwrapConversions() is IAnonymousFunctionOperation);
        if (predicateArgument == null ||
            predicateArgument.Value.UnwrapConversions() is not IAnonymousFunctionOperation lambdaOperation ||
            predicateArgument.Syntax is not ArgumentSyntax predicateSyntax)
        {
            return false;
        }

        if (predicateSyntax.Expression is not LambdaExpressionSyntax lambda ||
            lambda.Body is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.EqualsExpression))
        {
            return false;
        }

        if (!TryGetKeyValueExpression(binary, lambdaOperation, semanticModel, cancellationToken, out var valueExpression))
            return false;

        var cancellationTokenArgument = operation.Arguments
            .FirstOrDefault(argument => IsCancellationTokenParameter(argument.Parameter));

        var tokenSyntax = cancellationTokenArgument?.Syntax as ArgumentSyntax;
        fixContext = new FixContext(methodName, valueExpression, tokenSyntax);
        return true;
    }

    private static bool IsAwaited(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is AwaitExpressionSyntax)
                return true;

            if (current is ParenthesizedExpressionSyntax)
                continue;

            return false;
        }

        return false;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol? parameter)
    {
        return parameter?.Type.Name == nameof(CancellationToken) &&
               parameter.Type.ContainingNamespace?.ToString() == "System.Threading";
    }

    private sealed class FixContext
    {
        public FixContext(string methodName, ExpressionSyntax keyValueExpression, ArgumentSyntax? cancellationTokenArgument)
        {
            IsAsync = methodName.EndsWith("Async");
            KeyValueExpression = keyValueExpression;
            CancellationTokenArgument = cancellationTokenArgument;
        }

        public bool IsAsync { get; }

        public ExpressionSyntax KeyValueExpression { get; }

        public ArgumentSyntax? CancellationTokenArgument { get; }
    }
}
