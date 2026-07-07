using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private static readonly ImmutableHashSet<string> UnsupportedExecuteUpdateReceiverSteps =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Skip", "Take", "Distinct");

    private static ExpressionSyntax StripCollectionMaterializer(ExpressionSyntax expression)
    {
        // The analyzer's query walker unwraps `await`, so an inline async materializer such as
        // `await db.Users.Where(...).ToListAsync()` reaches the fixer; unwrap it before stripping.
        if (expression is AwaitExpressionSyntax awaitExpression)
            expression = awaitExpression.Expression;

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.ArgumentList.Arguments.Count == 0 &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            IsCollectionMaterializer(memberAccess.Name.Identifier.Text))
        {
            return memberAccess.Expression;
        }

        return expression;
    }

    private static bool IsCollectionMaterializer(string methodName) =>
        methodName is "ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync";

    private static bool HasUnsupportedExecuteUpdateReceiverStep(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = expression switch
            {
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                AwaitExpressionSyntax awaitExpression => awaitExpression.Expression,
                _ => expression
            };

            if (expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (UnsupportedExecuteUpdateReceiverSteps.Contains(memberAccess.Name.Identifier.Text))
                return true;

            if (IsStaticLinqTypeExpression(memberAccess.Expression) &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                HasUnsupportedExecuteUpdateReceiverStep(invocation.ArgumentList.Arguments[0].Expression))
            {
                return true;
            }

            expression = memberAccess.Expression;
        }
    }

    private static bool IsStaticLinqTypeExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text is "Queryable" or "Enumerable",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text is "Queryable" or "Enumerable",
            _ => false
        };
    }
}
