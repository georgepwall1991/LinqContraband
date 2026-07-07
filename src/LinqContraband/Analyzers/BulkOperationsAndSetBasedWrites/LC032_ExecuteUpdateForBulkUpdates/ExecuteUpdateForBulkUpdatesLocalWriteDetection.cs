using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static bool HasLocalWrites(ILocalSymbol local, SyntaxNode executableRootSyntax, SemanticModel semanticModel)
    {
        foreach (var assignment in executableRootSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, local))
                return true;
        }

        foreach (var prefix in executableRootSyntax.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!prefix.IsKind(SyntaxKind.PreIncrementExpression) &&
                !prefix.IsKind(SyntaxKind.PreDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(prefix.Operand).Symbol, local))
                return true;
        }

        foreach (var postfix in executableRootSyntax.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!postfix.IsKind(SyntaxKind.PostIncrementExpression) &&
                !postfix.IsKind(SyntaxKind.PostDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(postfix.Operand).Symbol, local))
                return true;
        }

        return false;
    }
}
