using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
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
        ImmutableArray<ISymbol> receiverPath,
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
            if (!MemberPathIsPrefix(entry.TargetPath, receiverPath)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(
                    scan, local, saveContext, receiverPath, entry.SpanStart, save))
            {
                if (IsRequiredOnPathFrom(mutation, entry.Operation, save))
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

            if (BranchHasUnconditionalReattach(ifStatement.Statement, matchingReattaches, mutation, save) &&
                BranchHasUnconditionalReattach(elseStatement, matchingReattaches, mutation, save))
            {
                return true;
            }
        }

        return false;
    }

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
            if (!branch.Span.Contains(entry.SpanStart) ||
                !BlockReaches(entry.Operation, save) ||
                (rejectCaughtThrow && HasCaughtThrowSkippingRequired(mutation, entry.Operation, save)))
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

    private static bool HasExhaustiveTryCatchReattach(
        List<ReattachEntry> matchingReattaches,
        IOperation mutation,
        IOperation save)
    {
        if (matchingReattaches.Count < 2) return false;

        foreach (var tryStatement in mutation.Syntax.AncestorsAndSelf().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(mutation.Syntax.SpanStart) ||
                tryStatement.Span.End >= save.Syntax.SpanStart ||
                tryStatement.Catches.Count == 0 ||
                !BranchHasUnconditionalReattach(
                    tryStatement.Block, matchingReattaches, mutation, save, rejectCaughtThrow: false))
            {
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

                if (StatementSkipsLater(catchClause.Block, save.Syntax))
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
        ImmutableArray<ISymbol> receiverPath,
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
        ImmutableArray<ISymbol> receiverPath,
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

            var coversMutationPath = entry.SpanStart < mutation.Syntax.SpanStart
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
        ImmutableArray<ISymbol> candidatePrefix,
        ImmutableArray<ISymbol> path)
    {
        if (candidatePrefix.Length > path.Length) return false;

        for (var i = 0; i < candidatePrefix.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidatePrefix[i], path[i]))
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
