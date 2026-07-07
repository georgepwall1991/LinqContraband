using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

public sealed partial class NPlusOneLooperFixer
{
    private static bool TryAddInclude(
        ExpressionSyntax querySourceExpression,
        LambdaExpressionSyntax navigationLambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenExpression)
    {
        rewrittenExpression = null!;

        if (querySourceExpression is not InvocationExpressionSyntax terminalInvocation ||
            terminalInvocation.Expression is not MemberAccessExpressionSyntax terminalMember)
        {
            return false;
        }

        var source = terminalMember.Expression;
        if (semanticModel.GetTypeInfo(source, cancellationToken).Type?.IsIQueryable() != true)
            return false;

        var includeMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source.WithoutTrivia(),
            SyntaxFactory.IdentifierName("Include"));

        var includeInvocation = SyntaxFactory.InvocationExpression(
                includeMember,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(navigationLambda.WithoutTrivia()))))
            .WithTriviaFrom(source);

        rewrittenExpression = terminalInvocation
            .WithExpression(terminalMember.WithExpression(includeInvocation))
            .WithTriviaFrom(querySourceExpression);

        return true;
    }
}
