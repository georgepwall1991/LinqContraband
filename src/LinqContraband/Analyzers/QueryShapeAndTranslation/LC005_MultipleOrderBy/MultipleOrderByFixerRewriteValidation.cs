using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

public sealed partial class MultipleOrderByFixer
{
    private static bool CanRewriteToThenBy(InvocationExpressionSyntax invocation, SemanticModel? semanticModel)
    {
        if (semanticModel == null)
            return false;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverExpression = GetLogicalReceiverExpression(invocation, memberAccess, semanticModel);
        if (receiverExpression == null)
            return false;

        var receiverType = semanticModel.GetTypeInfo(receiverExpression).Type;
        return IsOrderedSequence(receiverType);
    }

    private static ExpressionSyntax? GetLogicalReceiverExpression(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is INamedTypeSymbol type &&
            type.ContainingNamespace?.ToString() == "System.Linq" &&
            type.Name is "Enumerable" or "Queryable")
        {
            return invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : null;
        }

        return memberAccess.Expression;
    }

    private static bool IsOrderedSequence(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsOrderedSequenceType(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (IsOrderedSequenceType(iface))
                return true;
        }

        return false;
    }

    private static bool IsOrderedSequenceType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            return false;

        var ns = namedType.ContainingNamespace?.ToString();
        return ns == "System.Linq" && namedType.Name is "IOrderedEnumerable" or "IOrderedQueryable";
    }
}
