using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

public sealed partial class AnyOverCountFixer
{
    private static bool HasZeroConstant(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        return IsZeroConstant(binaryExpression.Left, semanticModel, cancellationToken) ||
               IsZeroConstant(binaryExpression.Right, semanticModel, cancellationToken);
    }

    private static bool IsZeroConstant(
        ExpressionSyntax expression,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel != null)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
                return IsZeroValue(constantValue.Value);
        }

        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
               literal.Token.ValueText == "0";
    }

    private static bool IsZeroValue(object? value)
    {
        if (value is int intValue)
            return intValue == 0;

        if (value is long longValue)
            return longValue == 0L;

        return false;
    }
}
