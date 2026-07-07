using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionFixer
{
    private static ExpressionSyntax RewriteInvocation(InvocationExpressionSyntax invocation, string propertyName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return invocation;

        var receiver = memberAccess.Expression.WithoutTrivia();
        var methodName = memberAccess.Name.Identifier.Text;
        var arguments = invocation.ArgumentList.Arguments;
        var predicateIndex = FindPredicateIndex(arguments);
        var tailArguments = predicateIndex >= 0
            ? arguments.Skip(predicateIndex + 1).Select(argument => argument.ToString()).ToArray()
            : arguments.Select(argument => argument.ToString()).ToArray();

        var builder = new System.Text.StringBuilder();
        builder.Append(receiver.ToString());

        if (predicateIndex >= 0)
        {
            builder.Append(".Where(");
            builder.Append(arguments[predicateIndex]);
            builder.Append(')');
        }

        builder.Append(".Select(x => x.");
        builder.Append(propertyName);
        builder.Append(')');
        builder.Append('.');
        builder.Append(methodName);
        builder.Append('(');
        builder.Append(string.Join(", ", tailArguments));
        builder.Append(')');

        return SyntaxFactory.ParseExpression(builder.ToString()).WithTriviaFrom(invocation);
    }

    private static int FindPredicateIndex(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
                return i;
        }

        return -1;
    }
}
