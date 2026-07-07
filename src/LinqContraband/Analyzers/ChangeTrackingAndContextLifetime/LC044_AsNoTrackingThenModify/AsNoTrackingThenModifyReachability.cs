using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool Dominates(IOperation earlier, IOperation later)
    {
        if (!earlier.SharesOwningExecutableRoot(later)) return false;
        if (earlier.Syntax.SpanStart >= later.Syntax.SpanStart) return false;

        var earlierBlock = FindEnclosingBlock(earlier);
        var laterBlock = FindEnclosingBlock(later);
        if (earlierBlock == null || laterBlock == null) return true;

        if (ReferenceEquals(earlierBlock, laterBlock))
        {
            if (IsNestedUnderOptionalControlFlow(earlier, earlierBlock, later))
                return false;

            return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, later.Syntax.SpanStart, later.Syntax);
        }

        if (IsBlockAncestor(laterBlock, earlierBlock))
        {
            if (IsNestedUnderOptionalControlFlow(earlier, laterBlock, later))
                return false;

            return BlockReaches(earlier, later);
        }

        if (!IsBlockAncestor(earlierBlock, laterBlock)) return false;

        var childInEarlierBlock = FindDirectChildOperationContainingSpan(earlierBlock.Operations, laterBlock.Syntax.Span);
        if (childInEarlierBlock == null) return false;
        if (earlier.Syntax.SpanStart >= childInEarlierBlock.Syntax.SpanStart) return false;

        return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, childInEarlierBlock.Syntax.SpanStart, later.Syntax);
    }

    private static bool BlockReaches(IOperation start, IOperation saveChanges)
    {
        // Local functions and lambdas are separate executable roots; do not let a mutation
        // inside one reach a SaveChanges in the enclosing method.
        if (!start.SharesOwningExecutableRoot(saveChanges)) return false;

        var startSyntax = start.Syntax;
        var saveSyntax = saveChanges.Syntax;
        if (saveSyntax.SpanStart <= startSyntax.SpanStart) return false;

        var startBlock = FindEnclosingBlock(start);
        var saveBlock = FindEnclosingBlock(saveChanges);
        if (startBlock == null || saveBlock == null) return true;

        if (ReferenceEquals(startBlock, saveBlock))
        {
            return !HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, saveSyntax.SpanStart, saveSyntax);
        }

        // Mutation in a nested block, SaveChanges in an ancestor block (e.g., mutation inside
        // an `if`/`using`/`while` body, save after that statement). Walk up the block chain and
        // verify every intermediate block can fall through from the mutation path to the save.
        if (IsBlockAncestor(saveBlock, startBlock))
        {
            var currentBlock = startBlock;
            while (currentBlock != null && !ReferenceEquals(currentBlock, saveBlock))
            {
                var afterSpan = ReferenceEquals(currentBlock, startBlock)
                    ? startSyntax.SpanStart
                    : currentBlock.Syntax.Span.Start;

                if (HasTerminatorBetween(currentBlock.Operations, afterSpan, currentBlock.Syntax.Span.End, saveSyntax))
                    return false;

                var parentBlock = FindEnclosingBlock(currentBlock);
                if (parentBlock == null) return false;

                var childInParent = FindDirectChildOperationContainingSpan(parentBlock.Operations, currentBlock.Syntax.Span);
                if (childInParent == null) return false;

                var beforeSpan = ReferenceEquals(parentBlock, saveBlock)
                    ? saveSyntax.SpanStart
                    : parentBlock.Syntax.Span.End;

                if (HasTerminatorBetween(parentBlock.Operations, childInParent.Syntax.Span.End, beforeSpan, saveSyntax))
                    return false;

                currentBlock = parentBlock;
            }

            return true;
        }

        // SaveChanges in a nested block, mutation in an ancestor block (e.g., mutation at the
        // top level, save inside a conditional branch that follows it).
        if (IsBlockAncestor(startBlock, saveBlock))
        {
            var containingOperation = FindDirectChildOperationContainingSpan(startBlock.Operations, saveBlock.Syntax.Span);
            if (containingOperation == null) return false;

            if (HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, containingOperation.Syntax.SpanStart, saveSyntax))
                return false;

            return !HasTerminatorBetween(saveBlock.Operations, saveBlock.Syntax.Span.Start, saveSyntax.SpanStart, saveSyntax);
        }

        // The blocks are siblings under a common ancestor (different branches of an `if`,
        // separate `case` labels, etc.) - there is no guaranteed path from one to the other.
        return false;
    }

}
