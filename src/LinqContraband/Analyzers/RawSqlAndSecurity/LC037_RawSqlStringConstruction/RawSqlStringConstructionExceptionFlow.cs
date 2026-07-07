using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool ThrowCanContinueThroughCatch(ThrowStatementSyntax throwStatement, int referenceStart)
    {
        foreach (var tryStatement in throwStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Catches.Count > 0 &&
                tryStatement.Span.End <= referenceStart &&
                ContainsNode(tryStatement.Block, throwStatement) &&
                CatchSequenceCanContinueThroughThrow(tryStatement, throwStatement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatchSequenceCanContinueThroughThrow(TryStatementSyntax tryStatement, ThrowStatementSyntax throwStatement)
    {
        foreach (var catchClause in tryStatement.Catches)
        {
            if (!CatchMayHandleThrow(catchClause, throwStatement))
                continue;

            var filterValue = GetCatchFilterConstantValue(catchClause.Filter);
            if (filterValue == false)
                continue;

            if (CatchCanReachReference(catchClause))
                return true;

            if (filterValue == true)
                return false;
        }

        return false;
    }

    private static bool CatchCanReachReference(CatchClauseSyntax catchClause)
    {
        return !StatementExitsMethod(catchClause.Block, catchClause.Block.Span.End);
    }

    private static bool CatchMayHandleThrow(CatchClauseSyntax catchClause, ThrowStatementSyntax throwStatement)
    {
        var catchType = GetResolvedSimpleTypeName(catchClause.Declaration?.Type, catchClause);
        if (catchType is null or "Exception")
            return true;

        if (throwStatement.Expression is not ObjectCreationExpressionSyntax objectCreation)
            return true;

        var thrownType = GetResolvedSimpleTypeName(objectCreation.Type, throwStatement);
        return thrownType == null ||
               catchType == thrownType ||
               IsKnownExceptionBase(catchType, thrownType) ||
               HasLocalExceptionBase(thrownType, catchType, throwStatement, depth: 0);
    }

    private static bool? GetCatchFilterConstantValue(CatchFilterClauseSyntax? filter)
    {
        return filter == null ? true : GetBooleanConstantValue(filter.FilterExpression);
    }

    private static bool? GetBooleanConstantValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
            ParenthesizedExpressionSyntax parenthesized => GetBooleanConstantValue(parenthesized.Expression),
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression) =>
                GetBooleanConstantValue(prefix.Operand) is { } value ? !value : null,
            _ => null
        };
    }
}
