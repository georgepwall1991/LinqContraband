using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool HasTransactionBoundaryBetween(int[] boundaries, int left, int right)
        {
            foreach (var boundary in boundaries)
            {
                if (boundary > left && boundary < right)
                    return true;
            }

            return false;
        }

        private static bool AreInsideSameTransactionUsing(SyntaxNode left, SyntaxNode right, int[] transactionBoundaries)
        {
            foreach (var usingStatement in left.AncestorsAndSelf().OfType<UsingStatementSyntax>())
            {
                if (!usingStatement.Statement.Span.Contains(right.SpanStart))
                    continue;

                if (ContainsTransactionBoundary(usingStatement.Span, transactionBoundaries))
                    return true;
            }

            foreach (var block in left.AncestorsAndSelf().OfType<BlockSyntax>())
            {
                if (!block.Span.Contains(right.SpanStart))
                    continue;

                foreach (var statement in block.Statements)
                {
                    if (statement is not LocalDeclarationStatementSyntax declaration)
                        continue;

                    if (!declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                        continue;

                    if (declaration.SpanStart >= left.SpanStart)
                        break;

                    if (ContainsTransactionBoundary(declaration.Span, transactionBoundaries))
                        return true;
                }
            }

            return false;
        }

        private static bool ContainsTransactionBoundary(TextSpan span, int[] transactionBoundaries)
        {
            foreach (var position in transactionBoundaries)
            {
                if (span.Contains(position))
                    return true;
            }

            return false;
        }

    }
}
