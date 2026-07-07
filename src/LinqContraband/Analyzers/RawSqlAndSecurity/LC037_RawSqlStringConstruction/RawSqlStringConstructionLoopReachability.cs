using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool IsLoopCarriedWriteForReference(IOperation writeOperation, IOperation reference)
    {
        if (writeOperation.Syntax.SpanStart <= reference.Syntax.SpanStart)
            return false;

        var current = reference.Parent;
        while (current != null)
        {
            if (current is ILoopOperation loop &&
                ContainsNode(loop.Syntax, writeOperation.Syntax))
            {
                return true;
            }

            current = current.Parent;
        }

        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            if (ContainsNode(loop, writeOperation.Syntax))
                return true;
        }

        return false;
    }

    private static bool IsLoopSyntax(SyntaxNode node)
    {
        return node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
    }

    private static bool CanWriteReachLaterLoopIteration(IOperation writeOperation, IOperation reference)
    {
        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            if (!ContainsNode(loop, writeOperation.Syntax))
                continue;

            foreach (var block in writeOperation.Syntax.Ancestors().OfType<BlockSyntax>())
            {
                if (!ContainsNode(loop, block) ||
                    !BlockTerminatesAfterNode(
                        block,
                        writeOperation.Syntax,
                        loop.Span.End))
                    continue;

                return false;
            }

            foreach (var switchSection in writeOperation.Syntax.Ancestors().OfType<SwitchSectionSyntax>())
            {
                if (!ContainsNode(loop, switchSection) ||
                    !SwitchSectionTerminatesAfterNode(
                        switchSection,
                        writeOperation.Syntax,
                        loop.Span.End))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        return false;
    }
}
