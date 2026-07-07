using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionFixer
{
    private static InvocationExpressionSyntax? FindEffectiveAsSingleQueryInvocation(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax? current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation &&
               currentInvocation.Expression is MemberAccessExpressionSyntax currentMemberAccess)
        {
            if (currentMemberAccess.Name.Identifier.Text == "AsSingleQuery")
                return currentInvocation;

            if (currentMemberAccess.Name.Identifier.Text == "AsSplitQuery")
                return null;

            current = currentMemberAccess.Expression;
        }

        return null;
    }

    private static InvocationExpressionSyntax? FindFirstIncludeInvocation(InvocationExpressionSyntax invocation)
    {
        InvocationExpressionSyntax? firstInclude = null;
        ExpressionSyntax? current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation &&
               currentInvocation.Expression is MemberAccessExpressionSyntax currentMemberAccess)
        {
            if (currentMemberAccess.Name.Identifier.Text == "Include")
                firstInclude = currentInvocation;

            current = currentMemberAccess.Expression;
        }

        return firstInclude;
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == methodName;
        }

        return false;
    }
}
