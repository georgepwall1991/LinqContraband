using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionFixer
{
    private static bool IsTrackedEntityConversionExpression(
        ExpressionSyntax expression,
        ILocalSymbol collectionVariable,
        HashSet<ILocalSymbol> trackedEntityLocals,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsTrackedEntityConversionExpression(parenthesized.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken),
            CastExpressionSyntax cast =>
                IsTrackedEntityExpression(cast.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken) ||
                IsTrackedEntityConversionExpression(cast.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) =>
                IsTrackedEntityExpression(binary.Left, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken) ||
                IsTrackedEntityConversionExpression(binary.Left, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool IsTrackedEntityExpression(
        ExpressionSyntax expression,
        ILocalSymbol collectionVariable,
        HashSet<ILocalSymbol> trackedEntityLocals,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return semanticModel.GetSymbolInfo(elementAccess.Expression, cancellationToken).Symbol is ILocalSymbol collectionSymbol &&
                   SymbolEqualityComparer.Default.Equals(collectionSymbol, collectionVariable);
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is ILocalSymbol localSymbol &&
               trackedEntityLocals.Contains(localSymbol);
    }
}
