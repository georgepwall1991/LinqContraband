using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
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

        private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
        {
            foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
            {
                if (!ifStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftBranch = GetContainingBranch(ifStatement, left);
                var rightBranch = GetContainingBranch(ifStatement, right);

                if (leftBranch != null &&
                    rightBranch != null &&
                    leftBranch != rightBranch)
                {
                    return true;
                }
            }

            foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
            {
                if (!switchStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftSection = GetContainingSwitchSection(switchStatement, left);
                var rightSection = GetContainingSwitchSection(switchStatement, right);

                if (leftSection != null &&
                    rightSection != null &&
                    leftSection != rightSection)
                {
                    return true;
                }
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

        private static StatementSyntax? GetContainingBranch(IfStatementSyntax ifStatement, SyntaxNode node)
        {
            if (ifStatement.Statement.Span.Contains(node.SpanStart))
                return ifStatement.Statement;

            if (ifStatement.Else?.Statement.Span.Contains(node.SpanStart) == true)
                return ifStatement.Else.Statement;

            return null;
        }

        private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
        {
            return switchStatement.Sections.FirstOrDefault(section => section.Span.Contains(node.SpanStart));
        }

        private static void Report(CompilationAnalysisContext context, InvocationRecord record, ISymbol contextSymbol, string methodName)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, record.Location, contextSymbol.Name, methodName));
        }
    }
}
