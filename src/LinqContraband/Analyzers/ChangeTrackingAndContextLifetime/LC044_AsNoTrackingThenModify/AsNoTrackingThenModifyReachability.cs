using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

            return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, later.Syntax.SpanStart, later);
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

        return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, childInEarlierBlock.Syntax.SpanStart, later);
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
            return !HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, saveSyntax.SpanStart, saveChanges);
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

                if (HasTerminatorBetween(currentBlock.Operations, afterSpan, currentBlock.Syntax.Span.End, saveChanges))
                    return false;

                var parentBlock = FindEnclosingBlock(currentBlock);
                if (parentBlock == null) return false;

                var childInParent = FindDirectChildOperationContainingSpan(parentBlock.Operations, currentBlock.Syntax.Span);
                if (childInParent == null) return false;

                var beforeSpan = ReferenceEquals(parentBlock, saveBlock)
                    ? saveSyntax.SpanStart
                    : parentBlock.Syntax.Span.End;

                if (HasTerminatorBetween(parentBlock.Operations, childInParent.Syntax.Span.End, beforeSpan, saveChanges))
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

            if (HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, containingOperation.Syntax.SpanStart, saveChanges))
                return false;

            return !HasTerminatorBetween(saveBlock.Operations, saveBlock.Syntax.Span.Start, saveSyntax.SpanStart, saveChanges);
        }

        // Ordinary sibling blocks are mutually exclusive, but a try block can transfer to
        // a matching catch and every completed try/catch path enters its finally block.
        return RelatedTryBlocksCanReach(start, saveChanges);
    }

    private static bool RelatedTryBlocksCanReach(IOperation start, IOperation later)
    {
        var semanticModel = start.SemanticModel ?? later.SemanticModel;
        if (semanticModel == null) return false;

        foreach (var tryStatement in start.Syntax.AncestorsAndSelf().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Finally?.Block.Span.Contains(later.Syntax.SpanStart) == true)
                return true;

            if (!tryStatement.Block.Span.Contains(start.Syntax.SpanStart))
                continue;

            var targetCatch = tryStatement.Catches.FirstOrDefault(catchClause =>
                catchClause.Block.Span.Contains(later.Syntax.SpanStart));
            if (targetCatch == null) continue;

            foreach (var throwSyntax in tryStatement.Block.DescendantNodes()
                         .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
            {
                if (throwSyntax.SpanStart <= start.Syntax.SpanStart ||
                    !ReferenceEquals(
                        throwSyntax.Ancestors().OfType<TryStatementSyntax>().FirstOrDefault(),
                        tryStatement) ||
                    !StartCanReachSyntax(start.Syntax, throwSyntax) ||
                    semanticModel.GetOperation(throwSyntax) is not IThrowOperation throwOperation)
                {
                    continue;
                }

                var exactThrownTypes = new List<ITypeSymbol>();
                if (TryCollectExactThrownTypes(throwOperation.Exception, exactThrownTypes))
                {
                    if (exactThrownTypes.Any(thrownType =>
                            ExactThrowCanReachCatch(thrownType, tryStatement, targetCatch, semanticModel)))
                    {
                        return true;
                    }

                    continue;
                }

                var openThrownType = GetThrownType(throwOperation, throwSyntax, semanticModel);
                if (openThrownType != null &&
                    OpenThrowCanReachCatch(openThrownType, tryStatement, targetCatch, semanticModel))
                {
                    return true;
                }
            }

            if (ImplicitTransferCanReachCatch(start, tryStatement, targetCatch, semanticModel) ||
                NestedThrowCanReachCatch(start, tryStatement, targetCatch, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplicitTransferCanReachCatch(
        IOperation start,
        TryStatementSyntax tryStatement,
        CatchClauseSyntax targetCatch,
        SemanticModel semanticModel)
    {
        var candidateType = targetCatch.Declaration == null
            ? semanticModel.Compilation.GetTypeByMetadataName("System.Exception")
            : semanticModel.GetTypeInfo(targetCatch.Declaration.Type).Type;
        if (candidateType == null) return false;

        var tryOperation = semanticModel.GetOperation(tryStatement.Block);
        if (tryOperation == null) return false;

        foreach (var operation in tryOperation.Descendants())
        {
            var containingThrow = operation.Syntax.Ancestors()
                .OfType<ThrowStatementSyntax>()
                .FirstOrDefault();
            var isThrowOperandOperation = containingThrow?.Expression?.Span.Contains(
                operation.Syntax.Span) == true;
            if (operation.Syntax.SpanStart <= start.Syntax.SpanStart ||
                start.Syntax.Span.Contains(operation.Syntax.Span) ||
                operation.Syntax.AncestorsAndSelf()
                    .Any(syntax => syntax is ThrowExpressionSyntax) ||
                containingThrow != null &&
                (!isThrowOperandOperation ||
                 !PotentialOperationCanReachCatch(
                     operation,
                     targetCatch,
                     tryStatement,
                     containingThrow,
                     start)) ||
                !IsImplicitlyPotentiallyThrowingOperation(operation) ||
                !start.SharesOwningExecutableRoot(operation) ||
                !StartCanReachSyntax(start.Syntax, operation.Syntax) ||
                !ExactExceptionEscapesNestedTries(
                    candidateType, operation.Syntax, tryStatement, semanticModel))
            {
                continue;
            }

            if (containingThrow != null ||
                ExactThrowCanReachCatch(candidateType, tryStatement, targetCatch, semanticModel))
                return true;
        }

        return false;
    }

    private static bool NestedThrowCanReachCatch(
        IOperation start,
        TryStatementSyntax tryStatement,
        CatchClauseSyntax targetCatch,
        SemanticModel semanticModel)
    {
        foreach (var throwSyntax in tryStatement.Block.DescendantNodes()
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (throwSyntax.SpanStart <= start.Syntax.SpanStart ||
                ReferenceEquals(
                    throwSyntax.Ancestors().OfType<TryStatementSyntax>().FirstOrDefault(),
                    tryStatement) ||
                !StartCanReachSyntax(start.Syntax, throwSyntax) ||
                semanticModel.GetOperation(throwSyntax) is not IThrowOperation throwOperation)
            {
                continue;
            }

            var exactThrownTypes = new List<ITypeSymbol>();
            if (TryCollectExactThrownTypes(throwOperation.Exception, exactThrownTypes))
            {
                if (exactThrownTypes.Any(thrownType =>
                        ExactExceptionEscapesNestedTries(
                            thrownType, throwSyntax, tryStatement, semanticModel) &&
                        ExactThrowCanReachCatch(
                            thrownType, tryStatement, targetCatch, semanticModel)))
                {
                    return true;
                }

                continue;
            }

            var openThrownType = GetThrownType(throwOperation, throwSyntax, semanticModel);
            if (openThrownType != null &&
                ExactExceptionEscapesNestedTries(
                    openThrownType, throwSyntax, tryStatement, semanticModel) &&
                OpenThrowCanReachCatch(openThrownType, tryStatement, targetCatch, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExactExceptionEscapesNestedTries(
        ITypeSymbol exceptionType,
        SyntaxNode throwingSyntax,
        TryStatementSyntax outerTry,
        SemanticModel semanticModel)
    {
        foreach (var nestedTry in throwingSyntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (ReferenceEquals(nestedTry, outerTry)) break;
            if (!nestedTry.Block.Span.Contains(throwingSyntax.SpanStart)) continue;

            foreach (var catchClause in nestedTry.Catches)
            {
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = semanticModel.GetConstantValue(filterExpression);
                    if (constant.HasValue && constant.Value is false) continue;
                    if (!constant.HasValue || constant.Value is not true) continue;
                }

                var caughtType = catchClause.Declaration == null
                    ? null
                    : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                if (CanCatchExactType(exceptionType, caughtType, catchClause))
                    return false;
            }
        }

        return true;
    }

    private static bool ExactThrowCanReachCatch(
        ITypeSymbol thrownType,
        TryStatementSyntax tryStatement,
        CatchClauseSyntax targetCatch,
        SemanticModel semanticModel)
    {
        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Filter?.FilterExpression is { } filterExpression)
            {
                var constant = semanticModel.GetConstantValue(filterExpression);
                if (constant.HasValue && constant.Value is false) continue;
            }

            var caughtType = catchClause.Declaration == null
                ? null
                : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (!CanCatchExactType(thrownType, caughtType, catchClause)) continue;

            if (ReferenceEquals(catchClause, targetCatch)) return true;

            if (catchClause.Filter == null) return false;
            var filterConstant = semanticModel.GetConstantValue(catchClause.Filter.FilterExpression);
            if (filterConstant.HasValue && filterConstant.Value is true) return false;
        }

        return false;
    }

    private static bool OpenThrowCanReachCatch(
        ITypeSymbol thrownType,
        TryStatementSyntax tryStatement,
        CatchClauseSyntax targetCatch,
        SemanticModel semanticModel)
    {
        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Filter?.FilterExpression is { } filterExpression)
            {
                var constant = semanticModel.GetConstantValue(filterExpression);
                if (constant.HasValue && constant.Value is false) continue;
            }

            var caughtType = catchClause.Declaration == null
                ? null
                : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (!CanPossiblyCatchOpenType(thrownType, caughtType, catchClause)) continue;

            return ReferenceEquals(catchClause, targetCatch);
        }

        return false;
    }

}
