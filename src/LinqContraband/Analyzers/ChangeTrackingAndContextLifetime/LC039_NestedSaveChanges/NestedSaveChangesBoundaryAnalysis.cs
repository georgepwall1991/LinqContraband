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
        /// <summary>
        /// <c>save0; if (flag) { BeginTransaction(); save1; return; } save2;</c>: the boundary sits in a branch that
        /// leaves the method before <c>save2</c>, so it never runs between <c>save0</c> and <c>save2</c>. A boundary only
        /// separates the pair when it can run on the path from the earlier save to the current one; it is discarded
        /// only when it is definitely off that path.
        /// </summary>
        private static bool HasTransactionBoundaryBetween(InvocationRecord[] boundaries, InvocationRecord previous, InvocationRecord current)
        {
            foreach (var boundary in boundaries)
            {
                if (boundary.Position <= previous.Position || boundary.Position >= current.Position)
                    continue;

                if (LeavesMethodBefore(boundary.Syntax, current.Syntax, boundary.Root.SemanticModel) ||
                    AreMutuallyExclusiveBranches(boundary.Syntax, current.Syntax, definitely: true) ||
                    AreMutuallyExclusiveBranches(previous.Syntax, boundary.Syntax, definitely: true))
                {
                    continue;
                }

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
