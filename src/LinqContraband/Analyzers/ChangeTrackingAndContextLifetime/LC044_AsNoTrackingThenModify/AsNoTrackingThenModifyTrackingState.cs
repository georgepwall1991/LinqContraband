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
            if (!ReattachCoversPath(entry, receiverPath)) continue;

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
        var eligibleReattaches = new List<ReattachEntry>();
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
                 (RequiredOperationCanBypassCollectiveReattach(
                      entry, matchingReattaches, mutation, save) ||
                  HasCaughtThrowSkippingRequired(mutation, entry.Operation, save) ||
                  CatchContainsCaughtThrowSkippingRequired(entry, save) ||
                  BranchContainsImplicitTransferBeforeReattach(
                      branch, entry.Operation, save))))
            {
                continue;
            }

            var hasBranchSkippingReattach = branch.DescendantNodes()
                .OfType<StatementSyntax>()
                .Any(statement => statement.SpanStart < entry.SpanStart &&
                                  statement is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax);
            if (!hasBranchSkippingReattach)
                eligibleReattaches.Add(entry);
        }

        return StatementGuaranteesReattach(branch, eligibleReattaches, save);
    }

    private static bool RequiredOperationCanBypassCollectiveReattach(
        ReattachEntry entry,
        List<ReattachEntry> matchingReattaches,
        IOperation mutation,
        IOperation save)
    {
        if (!RequiredOperationCanTransferBeforeCompletion(entry.Operation, save))
            return false;

        var hasSaveReachingHandler = false;
        foreach (var tryStatement in entry.Operation.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(entry.SpanStart) ||
                save.Syntax.SpanStart <= tryStatement.Span.End)
            {
                continue;
            }

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
                {
                    continue;
                }

                hasSaveReachingHandler = true;
                if (!BranchHasUnconditionalReattach(
                        catchClause.Block, matchingReattaches, mutation, save))
                {
                    return true;
                }
            }
        }

        return !hasSaveReachingHandler;
    }

    private static bool StatementGuaranteesReattach(
        StatementSyntax statement,
        List<ReattachEntry> eligibleReattaches,
        IOperation save)
    {
        switch (statement)
        {
            case ExpressionStatementSyntax:
                return eligibleReattaches.Any(entry => statement.Span.Contains(entry.SpanStart));

            case BlockSyntax block:
                return block.Statements.Any(child =>
                    StatementGuaranteesReattach(child, eligibleReattaches, save));

            case DoStatementSyntax doStatement:
                return StatementGuaranteesReattach(
                    doStatement.Statement, eligibleReattaches, save);

            case IfStatementSyntax ifStatement when ifStatement.Else?.Statement is { } elseStatement:
                return (StatementGuaranteesReattach(
                            ifStatement.Statement, eligibleReattaches, save) ||
                        StatementSkipsLater(ifStatement.Statement, save.Syntax)) &&
                       (StatementGuaranteesReattach(
                            elseStatement, eligibleReattaches, save) ||
                        StatementSkipsLater(elseStatement, save.Syntax));

            case LabeledStatementSyntax labeledStatement:
                return StatementGuaranteesReattach(
                    labeledStatement.Statement, eligibleReattaches, save);

            default:
                return false;
        }
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
                !reattach.SharesOwningExecutableRoot(operation) ||
                !StartCanReachSyntax(operation.Syntax, reattach.Syntax))
            {
                continue;
            }

            if (CanTransferToFallThroughCatch(
                    operation, save, reattach.Syntax.SpanStart))
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
        var matchingReattaches = new List<ReattachEntry>();
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= mutationSpan) continue;
            if (!ReattachCoversPath(entry, receiverPath)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                matchingReattaches.Add(entry);
            }
        }

        for (var i = 0; i < matchingReattaches.Count; i++)
        {
            var entry = matchingReattaches[i];
            if (PriorReattachDominatesMutation(entry.Operation, mutation) &&
                !RequiredPriorOperationCanBypassCollectiveReattach(
                    entry, matchingReattaches, mutation))
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

        var matchingReattaches = new List<ReattachEntry>();
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (!range.Contains(entry.Span)) continue;
            if (!ReattachCoversPath(entry, receiverPath)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                matchingReattaches.Add(entry);
            }
        }

        for (var i = 0; i < matchingReattaches.Count; i++)
        {
            var entry = matchingReattaches[i];
            var entryPrecedesMutation = entry.SpanStart < mutation.Syntax.SpanStart;
            if (!entryPrecedesMutation && !entry.PersistsExistingMutation) continue;

            var coversMutationPath = entryPrecedesMutation
                ? PriorReattachDominatesMutation(entry.Operation, mutation) &&
                  !RequiredPriorOperationCanBypassCollectiveReattach(
                      entry, matchingReattaches, mutation)
                : IsRequiredOnPathFrom(mutation, entry.Operation, save);
            if (!coversMutationPath) continue;

            return true;
        }

        return false;
    }

    private static bool RequiredPriorOperationCanBypassCollectiveReattach(
        ReattachEntry entry,
        List<ReattachEntry> matchingReattaches,
        IOperation mutation)
    {
        if (!RequiredOperationCanTransferBeforeCompletion(entry.Operation, mutation))
            return false;

        var hasMutationReachingHandler = false;
        foreach (var tryStatement in entry.Operation.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(entry.SpanStart) ||
                mutation.Syntax.SpanStart <= tryStatement.Span.End)
            {
                continue;
            }

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = mutation.SemanticModel?.GetConstantValue(filterExpression);
                    if (constant is { HasValue: true, Value: false })
                        continue;
                }

                if (StatementSkipsLater(catchClause.Block, mutation.Syntax) &&
                    !CatchHandlerCanReachLater(catchClause, mutation))
                {
                    continue;
                }

                hasMutationReachingHandler = true;
                if (!PriorBranchHasUnconditionalReattach(
                        catchClause.Block, matchingReattaches, mutation))
                {
                    return true;
                }
            }
        }

        return !hasMutationReachingHandler;
    }

    private static bool PriorBranchHasUnconditionalReattach(
        StatementSyntax branch,
        List<ReattachEntry> matchingReattaches,
        IOperation mutation)
    {
        var eligibleReattaches = new List<ReattachEntry>();
        for (var i = 0; i < matchingReattaches.Count; i++)
        {
            var entry = matchingReattaches[i];
            var reachesMutation = BlockReaches(entry.Operation, mutation) ||
                                  (branch.Parent is CatchClauseSyntax catchClause &&
                                   (!StatementSkipsLater(catchClause.Block, mutation.Syntax) ||
                                    CatchHandlerCanReachLater(catchClause, mutation)));
            if (!branch.Span.Contains(entry.SpanStart) ||
                !reachesMutation ||
                RequiredOperationCanTransferBeforeCompletion(entry.Operation, mutation))
            {
                continue;
            }

            var hasBranchSkippingReattach = branch.DescendantNodes()
                .OfType<StatementSyntax>()
                .Any(statement => statement.SpanStart < entry.SpanStart &&
                                  statement is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax);
            if (!hasBranchSkippingReattach)
                eligibleReattaches.Add(entry);
        }

        return StatementGuaranteesReattach(branch, eligibleReattaches, mutation);
    }

    private static bool PriorReattachDominatesMutation(
        IOperation reattach,
        IOperation mutation)
    {
        if (!Dominates(reattach, mutation)) return false;

        var catchClause = reattach.Syntax.Ancestors()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault();
        if (catchClause == null) return true;

        if (catchClause.Parent is not TryStatementSyntax tryStatement)
            return false;

        var semanticModel = reattach.SemanticModel ?? mutation.SemanticModel;
        var tryOperation = semanticModel?.GetOperation(tryStatement.Block);
        return tryOperation != null &&
               CatchClauseIsMandatoryFrom(catchClause, tryOperation, mutation);
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

    private static bool ReattachCoversPath(
        ReattachEntry entry,
        ImmutableArray<MemberPathSegment> receiverPath) =>
        (entry.CoversDescendantPaths || entry.TargetPath.Length == receiverPath.Length) &&
        MemberPathIsPrefix(entry.TargetPath, receiverPath);

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
