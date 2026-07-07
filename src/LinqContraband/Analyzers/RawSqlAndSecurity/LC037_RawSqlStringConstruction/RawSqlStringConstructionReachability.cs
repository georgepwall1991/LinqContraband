using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool IsRelevantWriteForReference(IOperation writeOperation, IOperation reference)
    {
        var referenceStart = reference.Syntax.SpanStart;
        return IsWriteBeforeReference(writeOperation, referenceStart) ||
               IsLoopCarriedWriteForReference(writeOperation, reference);
    }

    private static bool CanRelevantWriteReachReference(IOperation writeOperation, IOperation reference, int referenceStart)
    {
        return CanOperationReachReference(writeOperation, referenceStart) ||
               IsLoopCarriedWriteForReference(writeOperation, reference);
    }

    private static bool CanOperationReachReference(IOperation operation, int referenceStart)
    {
        foreach (var block in operation.Syntax.Ancestors().OfType<BlockSyntax>())
        {
            if (block.Span.End > referenceStart)
                continue;

            if (BlockTerminatesAfterNode(
                    block,
                    operation.Syntax,
                    referenceStart))
                return false;
        }

        foreach (var switchSection in operation.Syntax.Ancestors().OfType<SwitchSectionSyntax>())
        {
            if (switchSection.Span.End <= referenceStart &&
                SwitchSectionTerminatesAfterNode(
                    switchSection,
                    operation.Syntax,
                    referenceStart))
                return false;
        }

        return true;
    }

    private static bool ContainsNode(SyntaxNode container, SyntaxNode node)
    {
        return container.SpanStart <= node.SpanStart &&
               container.Span.End >= node.Span.End;
    }

    private static bool IsEarlierStatementInSameBlock(SyntaxNode writeNode, SyntaxNode referenceNode)
    {
        var writeStatement = writeNode.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        var referenceStatement = referenceNode.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (writeStatement?.Parent is not BlockSyntax block ||
            !ReferenceEquals(referenceStatement?.Parent, block))
        {
            return false;
        }

        var writeIndex = block.Statements.IndexOf(writeStatement);
        var referenceIndex = block.Statements.IndexOf(referenceStatement);
        return writeIndex >= 0 &&
               referenceIndex >= 0 &&
               writeIndex < referenceIndex;
    }

    private static bool IsDefinitelyExecutedBeforeReference(IOperation operation, IOperation executableRoot)
    {
        return IsGuaranteedBeforeReference(operation, executableRoot) &&
               !IsInsideShortCircuitRightOperand(operation, executableRoot);
    }

    private static bool IsResetGuaranteedBeforeReference(
        IOperation operation,
        IOperation executableRoot,
        int referenceStart)
    {
        return IsDefinitelyExecutedBeforeReference(operation, executableRoot) ||
               IsInsideGuaranteedFinallyBeforeReference(operation, executableRoot, referenceStart) ||
               IsInsideOnlySurvivingIfBranches(operation.Syntax, referenceStart);
    }

    private static bool IsInsideGuaranteedFinallyBeforeReference(
        IOperation operation,
        IOperation executableRoot,
        int referenceStart)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ITryOperation tryOperation &&
                tryOperation.Finally != null &&
                ContainsNode(tryOperation.Finally.Syntax, operation.Syntax))
            {
                var nested = operation.Parent;
                while (nested != null && !ReferenceEquals(nested, tryOperation))
                {
                    if (nested is IConditionalOperation or ISwitchOperation or ILoopOperation or ITryOperation)
                        return false;

                    nested = nested.Parent;
                }

                return tryOperation.Syntax.Span.End <= referenceStart &&
                       IsDefinitelyExecutedBeforeReference(tryOperation, executableRoot);
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsInsideShortCircuitRightOperand(IOperation operation, IOperation executableRoot)
    {
        var current = operation;
        var parent = current.Parent;

        while (parent != null && !ReferenceEquals(parent, executableRoot))
        {
            if (parent is IBinaryOperation binary &&
                (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ||
                 binary.OperatorKind == BinaryOperatorKind.ConditionalOr) &&
                ReferenceEquals(binary.RightOperand, current))
            {
                return true;
            }

            current = parent;
            parent = current.Parent;
        }

        return false;
    }
}
