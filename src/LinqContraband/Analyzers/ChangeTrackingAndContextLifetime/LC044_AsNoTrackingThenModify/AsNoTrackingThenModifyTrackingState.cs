using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool HasReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        IOperation mutation,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        var saveSpan = save.Syntax.SpanStart;
        var matchingReattaches = new List<ReattachEntry>();
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= mutation.Syntax.SpanStart || entry.SpanStart >= saveSpan) continue;
            if (!entry.PersistsExistingMutation) continue;
            if (!MemberPathIsPrefix(entry.TargetPath, receiverPath)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                if (IsRequiredOnPathFrom(mutation, entry.Operation, save) &&
                    !CatchContainsCaughtThrowSkippingRequired(entry, save))
                    return true;

                matchingReattaches.Add(entry);
            }
        }

        return HasExhaustiveIfBranchReattach(matchingReattaches, mutation, save) ||
               HasExhaustiveTryCatchReattach(matchingReattaches, mutation, save);
    }

    private static bool HasExhaustiveIfBranchReattach(
        List<ReattachEntry> matchingReattaches,
        IOperation mutation,
        IOperation save)
    {
        if (matchingReattaches.Count < 2) return false;

        foreach (var ifStatement in mutation.Syntax.SyntaxTree.GetRoot()
                     .DescendantNodes()
                     .OfType<IfStatementSyntax>())
        {
            if (ifStatement.SpanStart <= mutation.Syntax.SpanStart ||
                ifStatement.Span.End >= save.Syntax.SpanStart ||
                ifStatement.Else?.Statement is not { } elseStatement)
            {
                continue;
            }

            var ifOperation = mutation.SemanticModel?.GetOperation(ifStatement);
            if (ifOperation == null || !IsRequiredOnPathFrom(mutation, ifOperation, save))
                continue;

            var conditionOperation = mutation.SemanticModel?.GetOperation(ifStatement.Condition);
            if (conditionOperation != null &&
                (ConditionOperationCanBypassBranches(conditionOperation, save) ||
                 conditionOperation.Descendants().Any(operation =>
                     ConditionOperationCanBypassBranches(operation, save))))
            {
                continue;
            }

            if (BranchHasUnconditionalReattach(ifStatement.Statement, matchingReattaches, mutation, save) &&
                BranchHasUnconditionalReattach(elseStatement, matchingReattaches, mutation, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConditionOperationCanBypassBranches(IOperation operation, IOperation save) =>
        IsImplicitlyPotentiallyThrowingOperation(operation) &&
        CanTransferToFallThroughCatch(operation, save);

    private static bool BranchHasUnconditionalReattach(
        StatementSyntax branch,
        List<ReattachEntry> matchingReattaches,
        IOperation mutation,
        IOperation save,
        bool rejectCaughtThrow = true)
    {
        for (var i = 0; i < matchingReattaches.Count; i++)
        {
            var entry = matchingReattaches[i];
            var reachesSave = BlockReaches(entry.Operation, save) ||
                              (branch.Parent is CatchClauseSyntax catchClause &&
                               (!StatementSkipsLater(catchClause.Block, save.Syntax) ||
                                CatchHandlerCanReachLater(catchClause, save)));
            if (!branch.Span.Contains(entry.SpanStart) ||
                !reachesSave ||
                (rejectCaughtThrow &&
                 (HasCaughtThrowSkippingRequired(mutation, entry.Operation, save) ||
                  CatchContainsCaughtThrowSkippingRequired(entry, save) ||
                  BranchContainsImplicitTransferBeforeReattach(
                      branch, entry.Operation, save))))
            {
                continue;
            }

            if (branch is ExpressionStatementSyntax)
                return true;

            if (branch is not BlockSyntax block)
                continue;

            var directStatement = block.Statements.FirstOrDefault(statement =>
                statement.Span.Contains(entry.SpanStart));
            if (directStatement is not ExpressionStatementSyntax)
                continue;

            var hasBranchSkippingReattach = block.DescendantNodes()
                .OfType<StatementSyntax>()
                .Any(statement => statement.SpanStart < entry.SpanStart &&
                                  statement is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax);
            if (!hasBranchSkippingReattach)
                return true;
        }

        return false;
    }

    private static bool BranchContainsImplicitTransferBeforeReattach(
        StatementSyntax branch,
        IOperation reattach,
        IOperation save)
    {
        var branchOperation = reattach.SemanticModel?.GetOperation(branch);
        if (branchOperation == null) return false;

        foreach (var operation in branchOperation.Descendants())
        {
            if (operation.Syntax.SpanStart >= reattach.Syntax.SpanStart ||
                reattach.Syntax.Span.Contains(operation.Syntax.Span) ||
                operation.Syntax.AncestorsAndSelf()
                    .Any(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax) ||
                !IsImplicitlyPotentiallyThrowingOperation(operation) ||
                !reattach.SharesOwningExecutableRoot(operation))
            {
                continue;
            }

            if (CanTransferToFallThroughCatch(operation, save))
                return true;
        }

        return false;
    }

    private static bool HasExhaustiveTryCatchReattach(
        List<ReattachEntry> matchingReattaches,
        IOperation mutation,
        IOperation save)
    {
        if (matchingReattaches.Count < 2) return false;

        foreach (var tryStatement in mutation.Syntax.SyntaxTree.GetRoot()
                     .DescendantNodes()
                     .OfType<TryStatementSyntax>())
        {
            var mutationIsInTry = tryStatement.Block.Span.Contains(mutation.Syntax.SpanStart);
            var tryFollowsMutation = tryStatement.SpanStart > mutation.Syntax.SpanStart;
            if ((!mutationIsInTry && !tryFollowsMutation) ||
                tryStatement.Span.End >= save.Syntax.SpanStart ||
                tryStatement.Catches.Count == 0 ||
                !BranchHasUnconditionalReattach(
                    tryStatement.Block, matchingReattaches, mutation, save, rejectCaughtThrow: false))
            {
                continue;
            }

            if (tryFollowsMutation)
            {
                var tryOperation = mutation.SemanticModel?.GetOperation(tryStatement);
                if (tryOperation == null || !IsRequiredOnPathFrom(mutation, tryOperation, save))
                    continue;
            }

            var allHandlersCovered = true;
            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = mutation.SemanticModel?.GetConstantValue(filterExpression);
                    if (constant is { HasValue: true, Value: false })
                        continue;
                }

                if (StatementSkipsLater(catchClause.Block, save.Syntax) &&
                    !CatchHandlerCanReachLater(catchClause, save))
                    continue;

                if (!BranchHasUnconditionalReattach(
                        catchClause.Block, matchingReattaches, mutation, save))
                {
                    allHandlersCovered = false;
                    break;
                }
            }

            if (allHandlersCovered)
                return true;
        }

        return false;
    }

    private static bool HasDominatingPriorReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        int afterSpan,
        IOperation mutation,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        var mutationSpan = mutation.Syntax.SpanStart;
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= mutationSpan) continue;
            if (!MemberPathIsPrefix(entry.TargetPath, receiverPath)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                Dominates(entry.Operation, mutation) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReattachInRange(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        IOperation mutation,
        TextSpan range,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (!range.Contains(entry.Span)) continue;
            if (!MemberPathIsPrefix(entry.TargetPath, receiverPath)) continue;

            var entryPrecedesMutation = entry.SpanStart < mutation.Syntax.SpanStart;
            if (!entryPrecedesMutation && !entry.PersistsExistingMutation) continue;

            var coversMutationPath = entryPrecedesMutation
                ? Dominates(entry.Operation, mutation)
                : IsRequiredOnPathFrom(mutation, entry.Operation, save);
            if (!coversMutationPath) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MemberPathIsPrefix(
        ImmutableArray<MemberPathSegment> candidatePrefix,
        ImmutableArray<MemberPathSegment> path)
    {
        if (candidatePrefix.Length > path.Length) return false;

        for (var i = 0; i < candidatePrefix.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidatePrefix[i].Member, path[i].Member))
                return false;

            if (candidatePrefix[i].IsIndexer != path[i].IsIndexer)
                return false;

            if (candidatePrefix[i].IsIndexer &&
                (candidatePrefix[i].IndexKey == null ||
                 path[i].IndexKey == null ||
                 candidatePrefix[i].IndexKey != path[i].IndexKey))
                return false;
        }

        return true;
    }

    private static bool HasEarlierSaveChangesOnSameContext(
        AsNoTrackingThenModifyRootScan scan,
        ISymbol saveContext,
        IOperation mutation,
        IOperation currentSave)
    {
        var afterSpan = mutation.Syntax.SpanStart;
        var currentSaveSpan = currentSave.Syntax.SpanStart;
        var saves = scan.SaveChangesCalls;
        for (var i = 0; i < saves.Count; i++)
        {
            var entry = saves[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= currentSaveSpan) continue;
            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                IsRequiredOnPathFrom(mutation, entry.Operation, currentSave))
            {
                return true;
            }
        }

        return false;
    }
}
