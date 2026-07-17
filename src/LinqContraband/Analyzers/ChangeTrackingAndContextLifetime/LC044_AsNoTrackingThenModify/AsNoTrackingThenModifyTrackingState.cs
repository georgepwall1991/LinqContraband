using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                    scan, local, saveContext, receiverPath, entry.Operation, save))
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
                    scan, local, saveContext, receiverPath, entry.Operation, save))
            {
                matchingReattaches.Add(entry);
            }
        }

        for (var i = 0; i < matchingReattaches.Count; i++)
        {
            var entry = matchingReattaches[i];
            if (PriorReattachDominatesMutation(
                    entry.Operation, matchingReattaches, mutation) &&
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
                    scan, local, saveContext, receiverPath, entry.Operation, save))
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
                ? PriorReattachDominatesMutation(
                      entry.Operation, matchingReattaches, mutation) &&
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
        List<ReattachEntry> matchingReattaches,
        IOperation mutation)
    {
        var catchClause = reattach.Syntax.Ancestors()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault();
        if (catchClause == null) return Dominates(reattach, mutation);

        if (catchClause.Parent is not TryStatementSyntax tryStatement)
            return false;

        var semanticModel = reattach.SemanticModel ?? mutation.SemanticModel;
        var tryOperation = semanticModel?.GetOperation(tryStatement.Block);
        return tryOperation != null &&
               CatchClauseIsMandatoryFrom(catchClause, tryOperation, mutation) &&
               !HasAlternateMutationReachingCatchBeforeMandatoryThrow(
                   tryStatement, catchClause, tryOperation, matchingReattaches, mutation) &&
               !HasCaughtThrowSkippingRequired(
                   tryOperation, reattach, mutation);
    }

    private static bool HasAlternateMutationReachingCatchBeforeMandatoryThrow(
        TryStatementSyntax tryStatement,
        CatchClauseSyntax requiredCatch,
        IOperation tryOperation,
        List<ReattachEntry> matchingReattaches,
        IOperation mutation)
    {
        if (tryStatement.Block.Statements.LastOrDefault() is not ThrowStatementSyntax terminalThrow)
            return false;

        var semanticModel = tryOperation.SemanticModel ?? mutation.SemanticModel;
        var nullReferenceType = semanticModel?.Compilation
            .GetTypeByMetadataName("System.NullReferenceException");
        var terminalOperandMayBeNull = semanticModel != null &&
                                       TerminalThrowOperandMayBeNull(
                                           terminalThrow, semanticModel);

        foreach (var catchClause in tryStatement.Catches)
        {
            if (ReferenceEquals(catchClause, requiredCatch)) continue;

            if (catchClause.Filter?.FilterExpression is { } filterExpression)
            {
                var constant = mutation.SemanticModel?.GetConstantValue(filterExpression);
                if (constant is { HasValue: true, Value: false })
                    continue;
            }

            if ((StatementSkipsLater(catchClause.Block, mutation.Syntax) &&
                 !CatchHandlerCanReachLater(catchClause, mutation)) ||
                PriorBranchHasUnconditionalReattach(
                    catchClause.Block, matchingReattaches, mutation))
            {
                continue;
            }

            if (semanticModel != null &&
                nullReferenceType != null &&
                terminalOperandMayBeNull)
            {
                var caughtType = catchClause.Declaration == null
                    ? semanticModel.Compilation.GetTypeByMetadataName("System.Exception")
                    : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                if (ExactExceptionReachesCatch(
                        nullReferenceType, catchClause, tryStatement, semanticModel) &&
                    CanCatchExactType(nullReferenceType, caughtType, catchClause))
                {
                    return true;
                }
            }

            var canReachCatch = tryOperation.Descendants()
                .Any(operation =>
                    (operation.Syntax.SpanStart < terminalThrow.SpanStart ||
                     terminalThrow.Expression?.Span.Contains(operation.Syntax.Span) == true) &&
                    !operation.Syntax.AncestorsAndSelf()
                        .Any(syntax =>
                            syntax is ThrowExpressionSyntax ||
                            syntax is ThrowStatementSyntax throwStatement &&
                            !ReferenceEquals(throwStatement, terminalThrow)) &&
                    IsImplicitlyPotentiallyThrowingOperation(operation) &&
                    tryOperation.SharesOwningExecutableRoot(operation) &&
                    PotentialOperationCanReachCatch(
                        operation, catchClause, tryStatement, terminalThrow, mutation));
            if (canReachCatch) return true;
        }

        return false;
    }

    private static bool TerminalThrowOperandMayBeNull(
        ThrowStatementSyntax terminalThrow,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(terminalThrow) is not IThrowOperation throwOperation ||
            throwOperation.Exception == null)
        {
            return false;
        }

        return TerminalThrowOperandMayBeNull(throwOperation.Exception, semanticModel);
    }

    private static bool TerminalThrowOperandMayBeNull(
        IOperation? operation,
        SemanticModel semanticModel)
    {
        var terminalOperand = UnwrapTerminalThrowOperand(operation);
        return terminalOperand switch
        {
            IObjectCreationOperation => false,
            IThrowOperation => false,
            IConditionalOperation conditional =>
                TerminalThrowOperandMayBeNull(conditional.WhenTrue, semanticModel) ||
                TerminalThrowOperandMayBeNull(conditional.WhenFalse, semanticModel),
            ICoalesceOperation coalesce =>
                TerminalThrowOperandMayBeNull(coalesce.WhenNull, semanticModel),
            ILocalReferenceOperation localReference
                when TryGetUnchangedLocalInitializer(
                    localReference, semanticModel, out var initializer) =>
                TerminalThrowOperandMayBeNull(initializer, semanticModel),
            null => true,
            _ => semanticModel.GetTypeInfo(terminalOperand.Syntax)
                     .Nullability.FlowState != NullableFlowState.NotNull,
        };
    }

    private static bool TryGetUnchangedLocalInitializer(
        ILocalReferenceOperation localReference,
        SemanticModel semanticModel,
        out IOperation initializer)
    {
        initializer = null!;
        if (localReference.Local.DeclaringSyntaxReferences.Length != 1 ||
            localReference.Local.DeclaringSyntaxReferences[0].GetSyntax() is not
                VariableDeclaratorSyntax { Initializer.Value: { } initializerSyntax } ||
            localReference.FindOwningExecutableRoot() is not { } executableRoot ||
            HasLocalWriteBefore(
                executableRoot,
                localReference.Local,
                semanticModel,
                localReference.Syntax.SpanStart))
        {
            return false;
        }

        initializer = semanticModel.GetOperation(initializerSyntax)!;
        return initializer != null;
    }

    private static bool HasLocalWriteBefore(
        IOperation executableRoot,
        ILocalSymbol local,
        SemanticModel semanticModel,
        int beforeSpanStart)
    {
        foreach (var node in executableRoot.Syntax.DescendantNodes())
        {
            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                    postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                ArgumentSyntax argument when
                    !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
                _ => null,
            };
            if (target == null)
                continue;

            var targetOperation = semanticModel.GetOperation(target);
            if (targetOperation == null)
            {
                continue;
            }

            var sharesExecutableRoot = executableRoot.SharesOwningExecutableRoot(targetOperation);
            if (sharesExecutableRoot
                    ? targetOperation.Syntax.SpanStart >= beforeSpanStart
                    : !NestedExecutableIsInvokedBefore(
                        targetOperation, executableRoot, semanticModel, beforeSpanStart))
            {
                continue;
            }

            var writesLocal = targetOperation is ILocalReferenceOperation localTarget &&
                              SymbolEqualityComparer.Default.Equals(localTarget.Local, local) ||
                              target is TupleExpressionSyntax &&
                              targetOperation.DescendantsAndSelf()
                                  .OfType<ILocalReferenceOperation>()
                                  .Any(candidate => SymbolEqualityComparer.Default.Equals(
                                      candidate.Local, local));
            if (writesLocal)
            {
                return true;
            }
        }

        return false;
    }

    private static bool NestedExecutableIsInvokedBefore(
        IOperation nestedOperation,
        IOperation executableRoot,
        SemanticModel semanticModel,
        int beforeSpanStart,
        HashSet<SyntaxNode>? activeExecutables = null,
        int afterSpanEnd = int.MinValue)
    {
        var nestedExecutable = nestedOperation.Syntax.Ancestors().FirstOrDefault(syntax =>
            syntax is LocalFunctionStatementSyntax or
                LambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax);
        if (nestedExecutable == null)
            return false;

        activeExecutables ??= new HashSet<SyntaxNode>();
        if (!activeExecutables.Add(nestedExecutable))
            return false;

        try
        {
            return NestedExecutableIsInvokedBeforeCore(
                nestedExecutable,
                executableRoot,
                semanticModel,
                beforeSpanStart,
                activeExecutables,
                afterSpanEnd);
        }
        finally
        {
            activeExecutables.Remove(nestedExecutable);
        }
    }

    private static bool NestedExecutableIsInvokedBeforeCore(
        SyntaxNode nestedExecutable,
        IOperation executableRoot,
        SemanticModel semanticModel,
        int beforeSpanStart,
        HashSet<SyntaxNode> activeExecutables,
        int afterSpanEnd)
    {
        var bindings = nestedExecutable switch
        {
            LocalFunctionStatementSyntax localFunction
                when semanticModel.GetDeclaredSymbol(localFunction) is { } localFunctionSymbol =>
                new List<NestedExecutableBinding>
                {
                    new NestedExecutableBinding(localFunctionSymbol, sourceSyntax: null),
                },
            LambdaExpressionSyntax or AnonymousMethodExpressionSyntax =>
                GetNestedDelegateBindings(nestedExecutable, semanticModel),
            _ => new List<NestedExecutableBinding>(),
        };
        ExpandNestedDelegateAliases(bindings, executableRoot, semanticModel);
        if (bindings.Count == 0)
            return false;

        var terminalStatement = executableRoot.Syntax.FindToken(beforeSpanStart).Parent?
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (terminalStatement == null)
            return false;
        var terminalOperation = semanticModel.GetOperation(terminalStatement);
        if (terminalOperation == null)
            return false;

        foreach (var invocation in executableRoot.Syntax.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            var invocationOperation = semanticModel.GetOperation(invocation) as IInvocationOperation;
            var methodCandidate = semanticModel.GetSymbolInfo(invocation).Symbol;
            var delegateCandidate = invocationOperation == null
                ? null
                : GetInvokedDelegateSymbol(invocation, invocationOperation, semanticModel);
            var hasMatchingBinding = bindings.Any(binding =>
                SymbolEqualityComparer.Default.Equals(
                    binding.Symbol is IMethodSymbol ? methodCandidate : delegateCandidate,
                    binding.Symbol));
            var sharesExecutableRoot = invocationOperation != null &&
                                       executableRoot.SharesOwningExecutableRoot(invocationOperation);
            if (invocation.SpanStart >= beforeSpanStart ||
                invocation.SpanStart <= afterSpanEnd ||
                invocationOperation == null ||
                !hasMatchingBinding ||
                !ConstantGuardsAllowInvocation(
                    invocation,
                    executableRoot.Syntax,
                    owningForStatement: null,
                    semanticModel) ||
                !(sharesExecutableRoot
                    ? SyntaxMayReach(
                        invocationOperation.Syntax,
                        terminalOperation.Syntax,
                        semanticModel)
                    : NestedExecutableIsInvokedBefore(
                        invocationOperation,
                        executableRoot,
                        semanticModel,
                        beforeSpanStart,
                        activeExecutables,
                        afterSpanEnd)))
            {
                continue;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var candidate = binding.Symbol is IMethodSymbol
                    ? methodCandidate
                    : delegateCandidate;
                if (!SymbolEqualityComparer.Default.Equals(candidate, binding.Symbol))
                {
                    continue;
                }

                if (binding.SourceSyntax is not { } sourceSyntax)
                {
                    return true;
                }

                var reachesOnLaterIteration = SourceMayReachInvocationOnLaterIteration(
                    sourceSyntax,
                    invocation,
                    semanticModel);
                var sourceRunsAfterInvocation = SourceRunsAfterInvocationInForIterator(
                    sourceSyntax,
                    invocation);
                if ((sourceSyntax.Span.End >= invocation.SpanStart ||
                     sourceRunsAfterInvocation) &&
                    !reachesOnLaterIteration ||
                    SequentialBooleanGuardsAreMutuallyExclusive(
                        sourceSyntax,
                        invocation,
                        semanticModel) ||
                    semanticModel.GetOperation(sourceSyntax) is not { } sourceOperation ||
                    !reachesOnLaterIteration && !SyntaxMayReach(
                        sourceOperation.Syntax,
                        invocationOperation.Syntax,
                        semanticModel) ||
                    binding.Symbol is ILocalSymbol delegateLocal &&
                    HasDefiniteDelegateReplacementBetween(
                        executableRoot,
                        delegateLocal,
                        semanticModel,
                        sourceOperation,
                        invocationOperation))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool SourceMayReachInvocationOnLaterIteration(
        SyntaxNode sourceSyntax,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var sourceRunsAfterInvocation = SourceRunsAfterInvocationInForIterator(
            sourceSyntax,
            invocation);
        if (sourceSyntax.SpanStart <= invocation.SpanStart &&
            !sourceRunsAfterInvocation)
            return false;

        var loop = invocation.Ancestors().FirstOrDefault(ancestor =>
            (ancestor.IsKind(SyntaxKind.WhileStatement) ||
             ancestor.IsKind(SyntaxKind.DoStatement) ||
             ancestor.IsKind(SyntaxKind.ForStatement) ||
             ancestor.IsKind(SyntaxKind.ForEachStatement) ||
             ancestor.IsKind(SyntaxKind.ForEachVariableStatement)) &&
            ancestor.Span.Contains(sourceSyntax.SpanStart));
        var body = loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            ForEachVariableStatementSyntax forEachVariableStatement =>
                forEachVariableStatement.Statement,
            _ => null,
        };
        if (body == null)
            return false;

        var block = body as BlockSyntax;
        var invocationStatement = block?.Statements.FirstOrDefault(statement =>
                                      statement.Span.Contains(invocation.SpanStart)) ??
                                  (body.Span.Contains(invocation.SpanStart) ? body : null);
        if (invocationStatement == null)
        {
            return false;
        }

        if (sourceRunsAfterInvocation && loop is ForStatementSyntax loopForStatement)
        {
            if (InvocationPathDefinitelyPreventsNextIteration(
                    invocation,
                    loopForStatement,
                    semanticModel))
            {
                return false;
            }

            foreach (var statement in block?.Statements ?? Enumerable.Empty<StatementSyntax>())
            {
                if (statement.SpanStart > invocationStatement.SpanStart &&
                    StatementDefinitelyPreventsNextIteration(
                        statement,
                        loopForStatement,
                        semanticModel))
                {
                    return false;
                }
            }

            return !ForLoopRunsAtMostOnce(loopForStatement, semanticModel) &&
                   semanticModel.GetOperation(invocation) != null;
        }

        if (block == null)
            return false;

        var sourceStatement = block.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(sourceSyntax.SpanStart));
        if (sourceStatement == null)
            return false;

        foreach (var statement in block.Statements)
        {
            if (statement.SpanStart > invocationStatement.SpanStart &&
                statement.SpanStart < sourceStatement.SpanStart &&
                StatementDefinitelySkipsLater(statement, sourceSyntax, semanticModel))
            {
                return false;
            }

            if (statement.SpanStart <= sourceStatement.SpanStart)
                continue;

            if (StatementDefinitelyPreventsNextIteration(
                    statement,
                    loop!,
                    semanticModel))
            {
                return false;
            }
        }

        return semanticModel.GetOperation(invocation) != null;
    }

    private static bool SourceRunsAfterInvocationInForIterator(
        SyntaxNode sourceSyntax,
        InvocationExpressionSyntax invocation)
    {
        return invocation.Ancestors().OfType<ForStatementSyntax>().Any(forStatement =>
            forStatement.Statement.Span.Contains(invocation.SpanStart) &&
            forStatement.Incrementors.Any(incrementor =>
                incrementor.Span.Contains(sourceSyntax.SpanStart)));
    }

    private static bool ForLoopRunsAtMostOnce(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel)
    {
        if (forStatement.Declaration is not
            {
                Variables.Count: 1,
            } declaration ||
            declaration.Variables[0] is not
            {
                Initializer.Value: { } initializer,
            } variable ||
            semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol loopLocal ||
            !TryGetIntegralConstant(initializer, semanticModel, out var initialValue) ||
            forStatement.Condition is not BinaryExpressionSyntax condition ||
            !TryGetForLoopUnitStep(
                forStatement,
                loopLocal,
                semanticModel,
                out var step,
                out var stepSyntax) ||
            HasOtherLoopLocalWrite(
                forStatement,
                loopLocal,
                stepSyntax,
                semanticModel) ||
            !TryGetIntegralBounds(loopLocal.Type, out var minimum, out var maximum) ||
            initialValue < minimum ||
            initialValue > maximum)
        {
            return false;
        }

        if (!EvaluateIntegralCondition(
                condition,
                loopLocal,
                initialValue,
                semanticModel,
                out var initiallyTrue))
        {
            return false;
        }

        if (!initiallyTrue)
            return true;

        var wraps = initialValue == maximum && step > 0 ||
                    initialValue == minimum && step < 0;
        long nextValue;
        if (wraps)
        {
            if (semanticModel.GetOperation(stepSyntax) is not
                    IIncrementOrDecrementOperation stepOperation)
            {
                return false;
            }

            if (stepOperation.IsChecked)
                return true;

            nextValue = step > 0 ? minimum : maximum;
        }
        else
        {
            nextValue = initialValue + step;
        }

        return EvaluateIntegralCondition(
                   condition,
                   loopLocal,
                   nextValue,
                   semanticModel,
                   out var trueAfterFirstIteration) &&
               !trueAfterFirstIteration;
    }

    private static bool TryGetForLoopUnitStep(
        ForStatementSyntax forStatement,
        ILocalSymbol loopLocal,
        SemanticModel semanticModel,
        out long step,
        out ExpressionSyntax stepSyntax)
    {
        step = 0;
        stepSyntax = null!;
        var found = false;
        foreach (var incrementor in forStatement.Incrementors)
        {
            if (!TryGetUnitStep(
                    incrementor,
                    loopLocal,
                    semanticModel,
                    out var candidate))
            {
                continue;
            }

            if (found)
                return false;

            step = candidate;
            stepSyntax = incrementor;
            found = true;
        }

        return found;
    }

    private static bool HasOtherLoopLocalWrite(
        ForStatementSyntax forStatement,
        ILocalSymbol loopLocal,
        ExpressionSyntax stepSyntax,
        SemanticModel semanticModel)
    {
        var loopOperation = semanticModel.GetOperation(forStatement);
        var counterAliases = GetLoopCounterAliases(
            forStatement,
            loopLocal,
            loopOperation,
            semanticModel);
        var nodes = forStatement.Statement.DescendantNodesAndSelf()
            .Concat(forStatement.Incrementors.SelectMany(incrementor =>
                incrementor.DescendantNodesAndSelf()));
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, stepSyntax))
                continue;

            var nodeOperation = semanticModel.GetOperation(node);
            if (loopOperation != null &&
                nodeOperation != null &&
                !loopOperation.SharesOwningExecutableRoot(nodeOperation) &&
                !NestedExecutableMayBeInvokedInLoop(
                    nodeOperation,
                    forStatement,
                    loopOperation,
                    semanticModel))
            {
                continue;
            }

            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment
                    when assignment.Right is not RefExpressionSyntax =>
                    assignment.Left,
                PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                         prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                         postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                ArgumentSyntax argument
                    when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                         argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) =>
                    argument.Expression,
                _ => null,
            };
            if (target != null &&
                TargetWritesLoopCounter(target, counterAliases, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NestedExecutableMayBeInvokedInLoop(
        IOperation nestedOperation,
        ForStatementSyntax forStatement,
        IOperation loopOperation,
        SemanticModel semanticModel,
        HashSet<SyntaxNode>? activeExecutables = null)
    {
        var nestedExecutable = nestedOperation.Syntax.Ancestors().FirstOrDefault(syntax =>
            syntax is LocalFunctionStatementSyntax or
                LambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax);
        if (nestedExecutable == null)
            return false;

        activeExecutables ??= new HashSet<SyntaxNode>();
        if (!activeExecutables.Add(nestedExecutable))
            return false;

        try
        {
            return NestedExecutableMayBeInvokedInLoopCore(
                nestedExecutable,
                forStatement,
                loopOperation,
                semanticModel,
                activeExecutables);
        }
        finally
        {
            activeExecutables.Remove(nestedExecutable);
        }
    }

    private static bool NestedExecutableMayBeInvokedInLoopCore(
        SyntaxNode nestedExecutable,
        ForStatementSyntax forStatement,
        IOperation loopOperation,
        SemanticModel semanticModel,
        HashSet<SyntaxNode> activeExecutables)
    {
        var bindings = nestedExecutable switch
        {
            LocalFunctionStatementSyntax localFunction
                when semanticModel.GetDeclaredSymbol(localFunction) is { } localFunctionSymbol =>
                new List<NestedExecutableBinding>
                {
                    new NestedExecutableBinding(localFunctionSymbol, sourceSyntax: null),
                },
            LambdaExpressionSyntax or AnonymousMethodExpressionSyntax =>
                GetNestedDelegateBindings(nestedExecutable, semanticModel),
            _ => new List<NestedExecutableBinding>(),
        };
        ExpandNestedDelegateAliases(bindings, loopOperation, semanticModel);
        if (bindings.Count == 0)
            return false;

        var invocations = forStatement.Statement.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Concat(forStatement.Incrementors.SelectMany(incrementor =>
                incrementor.DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()));
        foreach (var invocation in invocations)
        {
            if (semanticModel.GetOperation(invocation) is not IInvocationOperation invocationOperation ||
                !InvocationMayRunWithinOwningExecutable(
                    invocation,
                    forStatement,
                    semanticModel))
            {
                continue;
            }

            var methodCandidate = semanticModel.GetSymbolInfo(invocation).Symbol;
            var delegateCandidate = GetInvokedDelegateSymbol(
                invocation,
                invocationOperation,
                semanticModel);
            foreach (var binding in bindings)
            {
                var candidate = binding.Symbol is IMethodSymbol
                    ? methodCandidate
                    : delegateCandidate;
                if (!SymbolEqualityComparer.Default.Equals(candidate, binding.Symbol) ||
                    !(loopOperation.SharesOwningExecutableRoot(invocationOperation) ||
                      NestedExecutableMayBeInvokedInLoop(
                        invocationOperation,
                        forStatement,
                        loopOperation,
                        semanticModel,
                        activeExecutables)))
                {
                    continue;
                }

                if (binding.SourceSyntax is not { } sourceSyntax)
                    return true;

                var sourceRunsBeforeIncrementorInvocation =
                    forStatement.Statement.Span.Contains(sourceSyntax.SpanStart) &&
                    forStatement.Incrementors.Any(incrementor =>
                        incrementor.Span.Contains(invocation.SpanStart));
                if (sourceSyntax.Span.End >= invocation.SpanStart &&
                    !sourceRunsBeforeIncrementorInvocation ||
                    SequentialBooleanGuardsAreMutuallyExclusive(
                        sourceSyntax,
                        invocation,
                        semanticModel) ||
                    semanticModel.GetOperation(sourceSyntax) is not { } sourceOperation ||
                    !sourceRunsBeforeIncrementorInvocation && !SyntaxMayReach(
                        sourceOperation.Syntax,
                        invocationOperation.Syntax,
                        semanticModel) ||
                    binding.Symbol is ILocalSymbol delegateLocal &&
                    HasDefiniteDelegateReplacementBetween(
                        loopOperation,
                        delegateLocal,
                        semanticModel,
                        sourceOperation,
                        invocationOperation,
                        forStatement))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool InvocationMayRunWithinOwningExecutable(
        InvocationExpressionSyntax invocation,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel)
    {
        var boundary = invocation.Ancestors().FirstOrDefault(syntax =>
                           syntax is LocalFunctionStatementSyntax or
                               LambdaExpressionSyntax or
                               AnonymousMethodExpressionSyntax) ??
                       forStatement.Statement;
        if (!ConstantGuardsAllowInvocation(
                invocation,
                boundary,
                forStatement,
                semanticModel))
        {
            return false;
        }

        var currentStatement = invocation.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        while (currentStatement != null && !ReferenceEquals(currentStatement, boundary))
        {
            if (currentStatement.Parent is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement.SpanStart >= currentStatement.SpanStart)
                        break;
                    if (StatementDefinitelySkipsLater(
                            statement,
                            invocation,
                            semanticModel))
                    {
                        return false;
                    }
                }
            }

            currentStatement = currentStatement.Ancestors()
                .OfType<StatementSyntax>()
                .FirstOrDefault();
        }

        return true;
    }

    private static bool ConstantGuardsAllowInvocation(
        InvocationExpressionSyntax invocation,
        SyntaxNode boundary,
        ForStatementSyntax? owningForStatement,
        SemanticModel semanticModel)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            switch (ancestor)
            {
                case IfStatementSyntax ifStatement
                    when semanticModel.GetConstantValue(ifStatement.Condition) is
                    { HasValue: true, Value: bool conditionValue }:
                    if (!conditionValue &&
                        ifStatement.Statement.Span.Contains(invocation.SpanStart) ||
                        conditionValue &&
                        ifStatement.Else?.Statement.Span.Contains(invocation.SpanStart) == true)
                    {
                        return false;
                    }

                    break;

                case ConditionalExpressionSyntax conditional
                    when semanticModel.GetConstantValue(conditional.Condition) is
                    { HasValue: true, Value: bool conditionValue }:
                    if (!conditionValue &&
                        conditional.WhenTrue.Span.Contains(invocation.SpanStart) ||
                        conditionValue &&
                        conditional.WhenFalse.Span.Contains(invocation.SpanStart))
                    {
                        return false;
                    }

                    break;

                case BinaryExpressionSyntax binary
                    when binary.Right.Span.Contains(invocation.SpanStart) &&
                         semanticModel.GetConstantValue(binary.Left) is
                         { HasValue: true, Value: bool leftValue }:
                    if (binary.IsKind(SyntaxKind.LogicalAndExpression) && !leftValue ||
                        binary.IsKind(SyntaxKind.LogicalOrExpression) && leftValue)
                    {
                        return false;
                    }

                    break;

                case WhileStatementSyntax whileStatement
                    when whileStatement.Statement.Span.Contains(invocation.SpanStart) &&
                         semanticModel.GetConstantValue(whileStatement.Condition) is
                         { HasValue: true, Value: false }:
                    return false;

                case ForStatementSyntax forStatement
                    when (forStatement.Statement.Span.Contains(invocation.SpanStart) ||
                          forStatement.Incrementors.Any(incrementor =>
                              incrementor.Span.Contains(invocation.SpanStart))) &&
                         forStatement.Condition != null &&
                         semanticModel.GetConstantValue(forStatement.Condition) is
                         { HasValue: true, Value: false }:
                    return false;
            }

            if (ReferenceEquals(ancestor, boundary) ||
                ReferenceEquals(ancestor, owningForStatement))
            {
                break;
            }
        }

        return true;
    }

    private static HashSet<ILocalSymbol> GetLoopCounterAliases(
        ForStatementSyntax forStatement,
        ILocalSymbol loopLocal,
        IOperation? loopOperation,
        SemanticModel semanticModel)
    {
        var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
        {
            loopLocal,
        };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var variable in forStatement.Statement.DescendantNodes()
                         .OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer?.Value is not RefExpressionSyntax refExpression ||
                    semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol alias ||
                    semanticModel.GetOperation(variable) is not { } variableOperation ||
                    loopOperation != null &&
                    !loopOperation.SharesOwningExecutableRoot(variableOperation) &&
                    !NestedExecutableMayBeInvokedInLoop(
                        variableOperation,
                        forStatement,
                        loopOperation,
                        semanticModel) ||
                    !ExpressionReferencesAnyLocal(
                        refExpression.Expression,
                        aliases,
                        semanticModel))
                {
                    continue;
                }

                changed |= aliases.Add(alias);
            }
        }

        return aliases;
    }

    private static bool TargetWritesLoopCounter(
        ExpressionSyntax target,
        HashSet<ILocalSymbol> counterAliases,
        SemanticModel semanticModel)
    {
        return target switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                TargetWritesLoopCounter(
                    parenthesized.Expression,
                    counterAliases,
                    semanticModel),
            TupleExpressionSyntax tuple => tuple.Arguments.Any(argument =>
                TargetWritesLoopCounter(
                    argument.Expression,
                    counterAliases,
                    semanticModel)),
            _ => ExpressionReferencesAnyLocal(target, counterAliases, semanticModel),
        };
    }

    private static bool ExpressionReferencesAnyLocal(
        ExpressionSyntax expression,
        HashSet<ILocalSymbol> locals,
        SemanticModel semanticModel)
    {
        return semanticModel.GetOperation(expression)?.UnwrapConversions() is
                   ILocalReferenceOperation localReference &&
               locals.Contains(localReference.Local);
    }

    private static bool TryGetUnitStep(
        ExpressionSyntax incrementor,
        ILocalSymbol loopLocal,
        SemanticModel semanticModel,
        out long step)
    {
        step = 0;
        ExpressionSyntax? operand = incrementor switch
        {
            PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
            _ => null,
        };
        if (operand == null ||
            semanticModel.GetOperation(operand)?.UnwrapConversions() is not
                ILocalReferenceOperation localReference ||
            !SymbolEqualityComparer.Default.Equals(localReference.Local, loopLocal))
        {
            return false;
        }

        step = incrementor.IsKind(SyntaxKind.PreDecrementExpression) ||
               incrementor.IsKind(SyntaxKind.PostDecrementExpression)
            ? -1
            : 1;
        return true;
    }

    private static bool EvaluateIntegralCondition(
        BinaryExpressionSyntax condition,
        ILocalSymbol loopLocal,
        long loopValue,
        SemanticModel semanticModel,
        out bool result)
    {
        result = false;
        if (!TryGetIntegralOperand(
                condition.Left,
                loopLocal,
                loopValue,
                semanticModel,
                out var left) ||
            !TryGetIntegralOperand(
                condition.Right,
                loopLocal,
                loopValue,
                semanticModel,
                out var right))
        {
            return false;
        }

        result = condition.Kind() switch
        {
            SyntaxKind.LessThanExpression => left < right,
            SyntaxKind.LessThanOrEqualExpression => left <= right,
            SyntaxKind.GreaterThanExpression => left > right,
            SyntaxKind.GreaterThanOrEqualExpression => left >= right,
            SyntaxKind.EqualsExpression => left == right,
            SyntaxKind.NotEqualsExpression => left != right,
            _ => false,
        };
        return condition.IsKind(SyntaxKind.LessThanExpression) ||
               condition.IsKind(SyntaxKind.LessThanOrEqualExpression) ||
               condition.IsKind(SyntaxKind.GreaterThanExpression) ||
               condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression) ||
               condition.IsKind(SyntaxKind.EqualsExpression) ||
               condition.IsKind(SyntaxKind.NotEqualsExpression);
    }

    private static bool TryGetIntegralOperand(
        ExpressionSyntax operand,
        ILocalSymbol loopLocal,
        long loopValue,
        SemanticModel semanticModel,
        out long value)
    {
        if (semanticModel.GetOperation(operand)?.UnwrapConversions() is
                ILocalReferenceOperation localReference &&
            SymbolEqualityComparer.Default.Equals(localReference.Local, loopLocal))
        {
            value = loopValue;
            return true;
        }

        return TryGetIntegralConstant(operand, semanticModel, out value);
    }

    private static bool TryGetIntegralConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out long value)
    {
        var constant = semanticModel.GetConstantValue(expression);
        switch (constant is { HasValue: true } ? constant.Value : null)
        {
            case sbyte candidate:
                value = candidate;
                return true;
            case byte candidate:
                value = candidate;
                return true;
            case short candidate:
                value = candidate;
                return true;
            case ushort candidate:
                value = candidate;
                return true;
            case int candidate:
                value = candidate;
                return true;
            case uint candidate:
                value = candidate;
                return true;
            case long candidate:
                value = candidate;
                return true;
            case char candidate:
                value = candidate;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryGetIntegralBounds(
        ITypeSymbol type,
        out long minimum,
        out long maximum)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
                minimum = sbyte.MinValue;
                maximum = sbyte.MaxValue;
                return true;
            case SpecialType.System_Byte:
                minimum = byte.MinValue;
                maximum = byte.MaxValue;
                return true;
            case SpecialType.System_Int16:
                minimum = short.MinValue;
                maximum = short.MaxValue;
                return true;
            case SpecialType.System_UInt16:
                minimum = ushort.MinValue;
                maximum = ushort.MaxValue;
                return true;
            case SpecialType.System_Int32:
                minimum = int.MinValue;
                maximum = int.MaxValue;
                return true;
            case SpecialType.System_UInt32:
                minimum = uint.MinValue;
                maximum = uint.MaxValue;
                return true;
            case SpecialType.System_Int64:
                minimum = long.MinValue;
                maximum = long.MaxValue;
                return true;
            case SpecialType.System_Char:
                minimum = char.MinValue;
                maximum = char.MaxValue;
                return true;
            default:
                minimum = 0;
                maximum = 0;
                return false;
        }
    }

    private static bool InvocationPathDefinitelyPreventsNextIteration(
        InvocationExpressionSyntax invocation,
        SyntaxNode loop,
        SemanticModel semanticModel)
    {
        var currentStatement = invocation.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        while (currentStatement != null && !ReferenceEquals(currentStatement, loop))
        {
            if (currentStatement.Parent is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement.SpanStart > currentStatement.SpanStart &&
                        StatementDefinitelyPreventsNextIteration(
                            statement,
                            loop,
                            semanticModel))
                    {
                        return true;
                    }
                }
            }

            currentStatement = currentStatement.Ancestors()
                .OfType<StatementSyntax>()
                .FirstOrDefault();
        }

        return false;
    }

    private static bool StatementDefinitelyPreventsNextIteration(
        StatementSyntax statement,
        SyntaxNode loop,
        SemanticModel semanticModel)
    {
        return statement switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax throwStatement =>
                ThrowDefinitelyPreventsNextIteration(
                    throwStatement,
                    loop,
                    semanticModel),
            GotoStatementSyntax gotoStatement =>
                GotoDefinitelyPreventsNextIteration(gotoStatement, loop),
            BreakStatementSyntax breakStatement => ReferenceEquals(
                breakStatement.Ancestors().FirstOrDefault(ancestor =>
                    ancestor is WhileStatementSyntax or
                        DoStatementSyntax or
                        ForStatementSyntax or
                        ForEachStatementSyntax or
                        ForEachVariableStatementSyntax or
                        SwitchStatementSyntax),
                loop),
            BlockSyntax { Statements.Count: > 0 } block =>
                StatementDefinitelyPreventsNextIteration(
                    block.Statements[block.Statements.Count - 1],
                    loop,
                    semanticModel),
            IfStatementSyntax { Else.Statement: { } elseStatement } ifStatement =>
                StatementDefinitelyPreventsNextIteration(
                    ifStatement.Statement,
                    loop,
                    semanticModel) &&
                StatementDefinitelyPreventsNextIteration(
                    elseStatement,
                    loop,
                    semanticModel),
            TryStatementSyntax tryStatement
                when tryStatement.Finally?.Block is { } finallyBlock &&
                     StatementDefinitelyPreventsNextIteration(
                         finallyBlock,
                         loop,
                         semanticModel) => true,
            TryStatementSyntax tryStatement =>
                StatementDefinitelyPreventsNextIteration(
                    tryStatement.Block,
                    loop,
                    semanticModel) &&
                tryStatement.Catches.All(catchClause =>
                    StatementDefinitelyPreventsNextIteration(
                        catchClause.Block,
                        loop,
                        semanticModel)),
            _ => false,
        };
    }

    private static bool ThrowDefinitelyPreventsNextIteration(
        ThrowStatementSyntax throwStatement,
        SyntaxNode loop,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(throwStatement) is not IThrowOperation throwOperation)
            return false;

        var thrownTypes = new List<ITypeSymbol>();
        if (GetThrownType(throwOperation, throwStatement, semanticModel) is { } thrownType)
            AddExactType(thrownTypes, thrownType);
        if (TerminalThrowOperandMayBeNull(throwStatement, semanticModel) &&
            semanticModel.Compilation.GetTypeByMetadataName(
                "System.NullReferenceException") is { } nullReferenceType)
        {
            AddExactType(thrownTypes, nullReferenceType);
        }

        return thrownTypes.Count > 0 && thrownTypes.All(type =>
            !ExactCaughtThrowMayReachNextIteration(
                type,
                throwStatement,
                loop,
                semanticModel));
    }

    private static bool ExactCaughtThrowMayReachNextIteration(
        ITypeSymbol thrownType,
        ThrowStatementSyntax throwStatement,
        SyntaxNode loop,
        SemanticModel semanticModel)
    {
        foreach (var tryStatement in throwStatement.Ancestors()
                     .OfType<TryStatementSyntax>())
        {
            if (!loop.Span.Contains(tryStatement.Span))
                break;
            if (!tryStatement.Block.Span.Contains(throwStatement.SpanStart))
                continue;

            if (tryStatement.Finally?.Block is { } finallyBlock &&
                StatementDefinitelyPreventsNextIteration(
                    finallyBlock,
                    loop,
                    semanticModel))
            {
                return false;
            }

            foreach (var catchClause in tryStatement.Catches)
            {
                var definitelyHandles = catchClause.Filter == null;
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = semanticModel.GetConstantValue(filterExpression);
                    if (constant is { HasValue: true, Value: false })
                        continue;
                    definitelyHandles = constant is { HasValue: true, Value: true };
                }

                var caughtType = catchClause.Declaration == null
                    ? null
                    : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                if (!CanCatchExactType(thrownType, caughtType, catchClause))
                    continue;

                if (!StatementDefinitelyPreventsNextIteration(
                        catchClause.Block,
                        loop,
                        semanticModel))
                {
                    return true;
                }

                if (definitelyHandles)
                    return false;
            }
        }

        return false;
    }

    private static bool GotoDefinitelyPreventsNextIteration(
        GotoStatementSyntax gotoStatement,
        SyntaxNode loop)
    {
        if (!gotoStatement.IsKind(SyntaxKind.GotoStatement) ||
            gotoStatement.Expression is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        var executableRoot = gotoStatement.Ancestors().FirstOrDefault(ancestor =>
            ancestor is BaseMethodDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AnonymousFunctionExpressionSyntax);
        var target = executableRoot?.DescendantNodes()
            .OfType<LabeledStatementSyntax>()
            .FirstOrDefault(label =>
                label.Identifier.ValueText == identifier.Identifier.ValueText);
        return target != null && !loop.Span.Contains(target.SpanStart);
    }

    private static bool SyntaxMayReach(
        SyntaxNode earlierSyntax,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        if (earlierSyntax.SyntaxTree != laterSyntax.SyntaxTree ||
            earlierSyntax.SpanStart >= laterSyntax.SpanStart ||
            !StartCanReachSyntax(earlierSyntax, laterSyntax) ||
            SequentialBooleanGuardsAreMutuallyExclusive(
                earlierSyntax,
                laterSyntax,
                semanticModel))
        {
            return false;
        }

        var earlierStatement = earlierSyntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        var laterStatement = laterSyntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (earlierStatement == null || laterStatement == null)
            return false;

        var commonBlock = earlierStatement.AncestorsAndSelf()
            .OfType<BlockSyntax>()
            .FirstOrDefault(block => block.Span.Contains(laterStatement.SpanStart));
        if (commonBlock == null)
            return false;

        var earlierChild = commonBlock.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(earlierStatement.SpanStart));
        var laterChild = commonBlock.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(laterStatement.SpanStart));
        if (earlierChild == null || laterChild == null)
            return false;

        var currentStatement = earlierStatement;
        while (currentStatement.Parent is BlockSyntax currentBlock &&
               currentBlock.Span != commonBlock.Span)
        {
            foreach (var statement in currentBlock.Statements)
            {
                if (statement.SpanStart > currentStatement.SpanStart &&
                    StatementDefinitelySkipsLater(statement, laterSyntax, semanticModel))
                {
                    return false;
                }
            }

            currentStatement = currentBlock.Ancestors()
                .OfType<StatementSyntax>()
                .FirstOrDefault()!;
            if (currentStatement == null)
                return false;
        }

        foreach (var statement in commonBlock.Statements)
        {
            if (statement.SpanStart <= earlierChild.SpanStart ||
                statement.SpanStart >= laterChild.SpanStart)
            {
                continue;
            }

            if (StatementDefinitelySkipsLater(statement, laterSyntax, semanticModel))
                return false;
        }

        return true;
    }

    private static bool SequentialBooleanGuardsAreMutuallyExclusive(
        SyntaxNode earlierSyntax,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        foreach (var earlierIf in earlierSyntax.AncestorsAndSelf()
                     .OfType<IfStatementSyntax>())
        {
            foreach (var laterIf in laterSyntax.AncestorsAndSelf()
                         .OfType<IfStatementSyntax>())
            {
                if (GuardPairIsMutuallyExclusive(
                        earlierIf,
                        earlierSyntax,
                        laterIf,
                        laterSyntax,
                        semanticModel))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool GuardPairIsMutuallyExclusive(
        IfStatementSyntax earlierIf,
        SyntaxNode earlierSyntax,
        IfStatementSyntax laterIf,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        if (earlierIf.Span == laterIf.Span ||
            earlierIf.Parent is not BlockSyntax earlierBlock ||
            laterIf.Parent is not BlockSyntax laterBlock ||
            earlierBlock.Span != laterBlock.Span ||
            !TryGetRequiredBooleanValue(
                earlierIf,
                earlierSyntax,
                semanticModel,
                out var earlierConditionSymbol,
                out var earlierValue) ||
            !TryGetRequiredBooleanValue(
                laterIf,
                laterSyntax,
                semanticModel,
                out var laterConditionSymbol,
                out var laterValue) ||
            !SymbolEqualityComparer.Default.Equals(
                earlierConditionSymbol,
                laterConditionSymbol) ||
            earlierValue == laterValue)
        {
            return false;
        }

        var root = semanticModel.GetOperation(earlierIf.Condition)?.FindOwningExecutableRoot();
        return root != null &&
               !HasDirectLocalWriteBetween(
                   root,
                   earlierConditionSymbol,
                   semanticModel,
                   earlierIf.Condition.Span.End,
                   laterIf.Condition.SpanStart);
    }

    private static bool TryGetRequiredBooleanValue(
        IfStatementSyntax ifStatement,
        SyntaxNode containedSyntax,
        SemanticModel semanticModel,
        out ISymbol symbol,
        out bool requiredValue)
    {
        symbol = null!;
        requiredValue = false;
        var inThen = ifStatement.Statement.Span.Contains(containedSyntax.SpanStart);
        var inElse = ifStatement.Else?.Statement.Span.Contains(containedSyntax.SpanStart) == true;
        if (!inThen && !inElse)
            return false;

        ExpressionSyntax condition = ifStatement.Condition;
        var positive = true;
        while (true)
        {
            switch (condition)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    condition = parenthesized.Expression;
                    continue;
                case PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.LogicalNotExpression):
                    positive = !positive;
                    condition = prefix.Operand;
                    continue;
            }

            break;
        }

        var conditionOperation = semanticModel.GetOperation(condition)?.UnwrapConversions();
        symbol = conditionOperation switch
        {
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            _ => null!,
        };
        if (symbol == null)
        {
            return false;
        }

        requiredValue = inThen ? positive : !positive;
        return true;
    }

    private static bool HasDirectLocalWriteBetween(
        IOperation executableRoot,
        ISymbol symbol,
        SemanticModel semanticModel,
        int afterSpanEnd,
        int beforeSpanStart)
    {
        foreach (var node in executableRoot.Syntax.DescendantNodes())
        {
            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                    postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                ArgumentSyntax argument when
                    !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
                _ => null,
            };
            if (target == null ||
                target.SpanStart <= afterSpanEnd ||
                target.SpanStart >= beforeSpanStart ||
                semanticModel.GetOperation(target) is not { } targetOperation)
            {
                continue;
            }

            ISymbol? targetSymbol = targetOperation switch
            {
                ILocalReferenceOperation localReference => localReference.Local,
                IParameterReferenceOperation parameterReference => parameterReference.Parameter,
                _ => null,
            };
            if (!SymbolEqualityComparer.Default.Equals(targetSymbol, symbol))
                continue;

            if (!executableRoot.SharesOwningExecutableRoot(targetOperation) &&
                !NestedExecutableIsInvokedBefore(
                    targetOperation,
                    executableRoot,
                    semanticModel,
                    beforeSpanStart,
                    afterSpanEnd: afterSpanEnd))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool StatementDefinitelySkipsLater(
        StatementSyntax statement,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        return statement switch
        {
            ThrowStatementSyntax throwStatement =>
                !CaughtThrowMayReachLater(throwStatement, laterSyntax, semanticModel),
            BlockSyntax block when block.Statements.Count > 0 =>
                StatementDefinitelySkipsLater(
                    block.Statements[block.Statements.Count - 1],
                    laterSyntax,
                    semanticModel),
            IfStatementSyntax { Else.Statement: { } elseStatement } ifStatement =>
                StatementDefinitelySkipsLater(ifStatement.Statement, laterSyntax, semanticModel) &&
                StatementDefinitelySkipsLater(elseStatement, laterSyntax, semanticModel),
            TryStatementSyntax tryStatement =>
                TryStatementDefinitelySkipsLater(tryStatement, laterSyntax, semanticModel),
            _ => StatementSkipsLater(statement, laterSyntax),
        };
    }

    private static bool TryStatementDefinitelySkipsLater(
        TryStatementSyntax tryStatement,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        if (tryStatement.Finally?.Block is { } finallyBlock &&
            StatementDefinitelySkipsLater(finallyBlock, laterSyntax, semanticModel))
        {
            return true;
        }

        if (!StatementDefinitelySkipsLater(tryStatement.Block, laterSyntax, semanticModel) ||
            semanticModel.GetOperation(tryStatement.Block) is not { } tryOperation ||
            semanticModel.GetOperation(laterSyntax) is not { } laterOperation)
        {
            return false;
        }

        foreach (var throwStatement in tryStatement.Block.DescendantNodes()
                     .OfType<ThrowStatementSyntax>())
        {
            if (!ReferenceEquals(
                    throwStatement.Ancestors().OfType<TryStatementSyntax>().FirstOrDefault(),
                    tryStatement))
            {
                continue;
            }

            if (CaughtThrowMayReachLater(throwStatement, laterSyntax, semanticModel))
                return false;
        }

        foreach (var operation in tryOperation.Descendants())
        {
            if (IsImplicitlyPotentiallyThrowingOperation(operation) &&
                CanTransferToFallThroughCatch(operation, laterOperation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CaughtThrowMayReachLater(
        ThrowStatementSyntax throwStatement,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(throwStatement) is not IThrowOperation throwOperation)
            return false;

        var thrownTypes = new List<ITypeSymbol>();
        if (GetThrownType(throwOperation, throwStatement, semanticModel) is { } thrownType)
            AddExactType(thrownTypes, thrownType);
        if (TerminalThrowOperandMayBeNull(throwStatement, semanticModel) &&
            semanticModel.Compilation.GetTypeByMetadataName(
                "System.NullReferenceException") is { } nullReferenceType)
        {
            AddExactType(thrownTypes, nullReferenceType);
        }

        return thrownTypes.Any(candidate => ExactCaughtThrowMayReachLater(
            candidate,
            throwStatement,
            laterSyntax,
            semanticModel));
    }

    private static bool ExactCaughtThrowMayReachLater(
        ITypeSymbol thrownType,
        ThrowStatementSyntax throwStatement,
        SyntaxNode laterSyntax,
        SemanticModel semanticModel)
    {
        foreach (var tryStatement in throwStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(throwStatement.SpanStart) ||
                laterSyntax.SpanStart <= tryStatement.Span.End)
            {
                continue;
            }

            foreach (var catchClause in tryStatement.Catches)
            {
                var definitelyHandles = catchClause.Filter == null;
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = semanticModel.GetConstantValue(filterExpression);
                    if (constant is { HasValue: true, Value: false })
                        continue;
                    definitelyHandles = constant is { HasValue: true, Value: true };
                }

                var caughtType = catchClause.Declaration == null
                    ? null
                    : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                if (!CanCatchExactType(thrownType, caughtType, catchClause))
                    continue;

                if (!StatementDefinitelySkipsLater(
                        catchClause.Block,
                        laterSyntax,
                        semanticModel))
                {
                    return true;
                }

                if (definitelyHandles)
                    return false;
            }
        }

        return false;
    }

    private static bool HasDefiniteDelegateReplacementBetween(
        IOperation executableRoot,
        ILocalSymbol local,
        SemanticModel semanticModel,
        IOperation source,
        IOperation invocation,
        ForStatementSyntax? loopWithIncrementorInvocation = null)
    {
        foreach (var node in executableRoot.Syntax.DescendantNodes())
        {
            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                         assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) =>
                    assignment.Left,
                _ => null,
            };
            if (target == null || semanticModel.GetOperation(target) is not { } targetOperation)
            {
                continue;
            }

            var writesLocal = targetOperation is ILocalReferenceOperation localTarget &&
                              SymbolEqualityComparer.Default.Equals(localTarget.Local, local) ||
                              target is TupleExpressionSyntax &&
                              targetOperation.DescendantsAndSelf()
                                  .OfType<ILocalReferenceOperation>()
                                  .Any(candidate => SymbolEqualityComparer.Default.Equals(
                                      candidate.Local, local));
            if (!writesLocal)
                continue;

            var assignmentNode = node as AssignmentExpressionSyntax;
            var rightOperation = assignmentNode == null
                ? null
                : semanticModel.GetOperation(assignmentNode.Right)?.UnwrapConversions();
            var matchedSelfRemoval = assignmentNode?.IsKind(
                                         SyntaxKind.SubtractAssignmentExpression) == true &&
                                     rightOperation is ILocalReferenceOperation removedLocal &&
                                     SymbolEqualityComparer.Default.Equals(removedLocal.Local, local);
            if (assignmentNode?.IsKind(SyntaxKind.SubtractAssignmentExpression) == true &&
                !matchedSelfRemoval)
            {
                continue;
            }

            if (!matchedSelfRemoval &&
                assignmentNode?.IsKind(SyntaxKind.SimpleAssignmentExpression) == true &&
                rightOperation != null &&
                rightOperation.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(candidate => SymbolEqualityComparer.Default.Equals(
                        candidate.Local, local)))
            {
                continue;
            }

            if (executableRoot.SharesOwningExecutableRoot(targetOperation))
            {
                var replacementOperation = semanticModel.GetOperation(node) ?? targetOperation;
                if ((IsRequiredOnPathFrom(source, replacementOperation, invocation) ||
                     loopWithIncrementorInvocation != null &&
                     IsDirectReplacementRequiredBeforeIncrementor(
                         source,
                         replacementOperation,
                         invocation,
                         loopWithIncrementorInvocation,
                         semanticModel)) &&
                    !DelegateReplacementCanTransferBeforeCompletion(
                        replacementOperation,
                        invocation))
                {
                    return true;
                }
            }
            else if (NestedLocalFunctionIsRequiredBetween(
                         targetOperation,
                         executableRoot,
                         semanticModel,
                         source,
                         invocation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDirectReplacementRequiredBeforeIncrementor(
        IOperation source,
        IOperation replacement,
        IOperation invocation,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel)
    {
        if (!forStatement.Statement.Span.Contains(source.Syntax.SpanStart) ||
            !forStatement.Statement.Span.Contains(replacement.Syntax.SpanStart) ||
            !forStatement.Incrementors.Any(incrementor =>
                incrementor.Span.Contains(invocation.Syntax.SpanStart)) ||
            forStatement.Statement is not BlockSyntax body)
        {
            return false;
        }

        var sourceStatement = source.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => ReferenceEquals(statement.Parent, body));
        var replacementStatement = replacement.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => ReferenceEquals(statement.Parent, body));
        if (sourceStatement == null ||
            replacementStatement == null ||
            replacementStatement is not ExpressionStatementSyntax ||
            sourceStatement.SpanStart >= replacementStatement.SpanStart)
        {
            return false;
        }

        foreach (var statement in body.Statements)
        {
            if (statement.SpanStart <= sourceStatement.SpanStart ||
                statement.SpanStart >= replacementStatement.SpanStart)
            {
                continue;
            }

            if (StatementDefinitelySkipsLater(
                    statement,
                    replacementStatement,
                    semanticModel))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DelegateReplacementCanTransferBeforeCompletion(
        IOperation replacement,
        IOperation later)
    {
        return replacement.DescendantsAndSelf().Any(operation =>
            replacement.SharesOwningExecutableRoot(operation) &&
            IsImplicitlyPotentiallyThrowingOperation(operation) &&
            CanTransferToFallThroughCatch(operation, later));
    }

    private static bool NestedLocalFunctionIsRequiredBetween(
        IOperation nestedOperation,
        IOperation executableRoot,
        SemanticModel semanticModel,
        IOperation source,
        IOperation later)
    {
        var localFunction = nestedOperation.Syntax.Ancestors()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault();
        if (localFunction == null ||
            semanticModel.GetDeclaredSymbol(localFunction) is not { } localFunctionSymbol ||
            !NestedWriteIsRequired(nestedOperation, localFunction, semanticModel, later))
        {
            return false;
        }

        foreach (var invocation in executableRoot.Syntax.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetOperation(invocation) is not IInvocationOperation invocationOperation ||
                !executableRoot.SharesOwningExecutableRoot(invocationOperation) ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(invocation).Symbol, localFunctionSymbol))
            {
                continue;
            }

            if (IsRequiredOnPathFrom(source, invocationOperation, later))
                return true;
        }

        return false;
    }

    private static bool NestedWriteIsRequired(
        IOperation nestedOperation,
        LocalFunctionStatementSyntax localFunction,
        SemanticModel semanticModel,
        IOperation later)
    {
        var isExpressionBody = localFunction.ExpressionBody?.Expression.Span.Contains(
            nestedOperation.Syntax.SpanStart) == true;
        if (!isExpressionBody)
        {
            for (var ancestor = nestedOperation.Syntax.Parent;
                 ancestor != null && !ReferenceEquals(ancestor, localFunction);
                 ancestor = ancestor.Parent)
            {
                if (ancestor is IfStatementSyntax or
                    SwitchStatementSyntax or
                    SwitchSectionSyntax or
                    WhileStatementSyntax or
                    ForStatementSyntax or
                    ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or
                    CatchClauseSyntax or
                    ConditionalExpressionSyntax or
                    SwitchExpressionSyntax)
                {
                    return false;
                }
            }
        }

        var assignmentSyntax = nestedOperation.Syntax.AncestorsAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault();
        if (assignmentSyntax != null &&
            semanticModel.GetOperation(assignmentSyntax) is { } replacementOperation &&
            DelegateReplacementCanTransferBeforeCompletion(replacementOperation, later))
        {
            return false;
        }

        if (localFunction.DescendantNodes()
            .OfType<StatementSyntax>()
            .Any(statement => statement.SpanStart < nestedOperation.Syntax.SpanStart &&
                              statement is ReturnStatementSyntax or
                                  ThrowStatementSyntax or
                                  GotoStatementSyntax or
                                  BreakStatementSyntax or
                                  ContinueStatementSyntax))
        {
            return false;
        }

        var executableRoot = nestedOperation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        foreach (var operation in executableRoot.Descendants())
        {
            if (operation.Syntax.SpanStart >= nestedOperation.Syntax.SpanStart ||
                operation.Syntax.Span.Contains(nestedOperation.Syntax.Span) ||
                !executableRoot.SharesOwningExecutableRoot(operation) ||
                !StartCanReachSyntax(operation.Syntax, nestedOperation.Syntax) ||
                !IsImplicitlyPotentiallyThrowingOperation(operation))
            {
                continue;
            }

            if (CanTransferToFallThroughCatch(
                    operation,
                    later,
                    nestedOperation.Syntax.SpanStart))
            {
                return false;
            }
        }

        return true;
    }

    private static List<NestedExecutableBinding> GetNestedDelegateBindings(
        SyntaxNode nestedExecutable,
        SemanticModel semanticModel)
    {
        var bindings = new List<NestedExecutableBinding>();
        foreach (var ancestor in nestedExecutable.Ancestors())
        {
            if (ancestor is LambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax)
            {
                break;
            }

            if (IsDelegateCreationPart(ancestor, semanticModel))
                continue;

            ISymbol? symbol;
            switch (ancestor)
            {
                case ParenthesizedExpressionSyntax:
                case CastExpressionSyntax:
                case EqualsValueClauseSyntax:
                case ConditionalExpressionSyntax:
                case SwitchExpressionArmSyntax:
                case SwitchExpressionSyntax:
                    continue;

                case VariableDeclaratorSyntax declarator:
                    symbol = semanticModel.GetDeclaredSymbol(declarator);
                    break;

                case AssignmentExpressionSyntax assignment
                    when (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                          assignment.IsKind(SyntaxKind.AddAssignmentExpression)) &&
                         semanticModel.GetOperation(assignment.Left) is
                        ILocalReferenceOperation localReference:
                    symbol = localReference.Local;
                    break;

                default:
                    return bindings;
            }

            if (symbol != null &&
                !bindings.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.Symbol, symbol)))
            {
                bindings.Add(new NestedExecutableBinding(symbol, ancestor));
            }
        }

        return bindings;
    }

    private static InvocationExpressionSyntax? GetDirectDelegateInvocation(
        SyntaxNode nestedExecutable,
        SemanticModel semanticModel)
    {
        if (nestedExecutable is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax))
            return null;

        var expression = nestedExecutable;
        while (expression.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax ||
               expression.Parent is PostfixUnaryExpressionSyntax postfix &&
               postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = expression.Parent;
        }

        var invocation = expression.Parent switch
        {
            InvocationExpressionSyntax directInvocation
                when ReferenceEquals(directInvocation.Expression, expression) =>
                directInvocation,
            MemberAccessExpressionSyntax memberAccess
                when ReferenceEquals(memberAccess.Expression, expression) &&
                     memberAccess.Parent is InvocationExpressionSyntax explicitInvocation &&
                     ReferenceEquals(explicitInvocation.Expression, memberAccess) =>
                explicitInvocation,
            _ => null,
        };
        return invocation != null &&
               semanticModel.GetOperation(invocation) is
                   IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke }
            ? invocation
            : null;
    }

    private static void ExpandNestedDelegateAliases(
        List<NestedExecutableBinding> bindings,
        IOperation executableRoot,
        SemanticModel semanticModel)
    {
        bool added;
        do
        {
            added = false;
            foreach (var node in executableRoot.Syntax.DescendantNodes())
            {
                ILocalSymbol? alias;
                ExpressionSyntax? value;
                switch (node)
                {
                    case VariableDeclaratorSyntax
                    {
                        Initializer.Value: { } initializer,
                    } declarator when semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol local:
                        alias = local;
                        value = initializer;
                        break;

                    case AssignmentExpressionSyntax assignment
                        when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                             semanticModel.GetOperation(assignment.Left) is
                                 ILocalReferenceOperation localReference:
                        alias = localReference.Local;
                        value = assignment.Right;
                        break;

                    default:
                        continue;
                }

                var sourceSymbols = GetPossibleDelegateSourceSymbols(
                        semanticModel.GetOperation(value))
                    .Distinct(SymbolEqualityComparer.Default)
                    .ToArray();
                if (sourceSymbols.Length == 0 ||
                    semanticModel.GetOperation(node) is not { } aliasSourceOperation ||
                    !executableRoot.SharesOwningExecutableRoot(aliasSourceOperation))
                {
                    continue;
                }

                foreach (var sourceSymbol in sourceSymbols)
                {
                    if (SymbolEqualityComparer.Default.Equals(alias, sourceSymbol))
                    {
                        continue;
                    }

                    var existingBindings = bindings.ToArray();
                    foreach (var binding in existingBindings)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(binding.Symbol, sourceSymbol) ||
                            bindings.Any(candidate =>
                                SymbolEqualityComparer.Default.Equals(candidate.Symbol, alias) &&
                                candidate.SourceSyntax?.Span == node.Span))
                        {
                            continue;
                        }

                        if (binding.SourceSyntax is { } bindingSource)
                        {
                            if (semanticModel.GetOperation(bindingSource) is not { } bindingSourceOperation ||
                                !(bindingSource.Span.End < node.SpanStart
                                    ? SyntaxMayReach(
                                        bindingSourceOperation.Syntax,
                                        aliasSourceOperation.Syntax,
                                        semanticModel)
                                    : BindingSourceMayReachAliasOnLaterIteration(
                                        bindingSource,
                                        node,
                                        bindingSourceOperation,
                                        aliasSourceOperation,
                                        binding.Symbol,
                                        semanticModel)) ||
                                binding.Symbol is ILocalSymbol sourceLocal &&
                                HasDefiniteDelegateReplacementBetween(
                                    executableRoot,
                                    sourceLocal,
                                    semanticModel,
                                    bindingSourceOperation,
                                    aliasSourceOperation))
                            {
                                continue;
                            }
                        }

                        bindings.Add(new NestedExecutableBinding(alias, node));
                        added = true;
                    }
                }
            }
        } while (added);
    }

    private static IEnumerable<ISymbol> GetPossibleDelegateSourceSymbols(IOperation? operation)
    {
        operation = operation?.UnwrapConversions();
        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                yield return localReference.Local;
                yield break;

            case IMethodReferenceOperation methodReference:
                yield return methodReference.Method;
                yield break;

            case IConditionalOperation conditional:
                var selectedBranch = conditional.Condition.ConstantValue is
                { HasValue: true, Value: bool conditionValue }
                        ? conditionValue ? conditional.WhenTrue : conditional.WhenFalse
                        : null;
                if (selectedBranch != null)
                {
                    foreach (var symbol in GetPossibleDelegateSourceSymbols(selectedBranch))
                        yield return symbol;
                    yield break;
                }

                foreach (var symbol in GetPossibleDelegateSourceSymbols(conditional.WhenTrue))
                    yield return symbol;
                foreach (var symbol in GetPossibleDelegateSourceSymbols(conditional.WhenFalse))
                    yield return symbol;
                yield break;
        }
    }

    private static bool BindingSourceMayReachAliasOnLaterIteration(
        SyntaxNode bindingSource,
        SyntaxNode aliasSource,
        IOperation bindingSourceOperation,
        IOperation aliasSourceOperation,
        ISymbol sourceSymbol,
        SemanticModel semanticModel)
    {
        if (sourceSymbol is not ILocalSymbol sourceLocal)
            return false;

        var forStatement = aliasSource.Ancestors().OfType<ForStatementSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Statement.Span.Contains(bindingSource.SpanStart));
        if (forStatement?.Statement is not BlockSyntax body ||
            body.Statements.FirstOrDefault(statement =>
                statement.Span.Contains(aliasSource.SpanStart)) is not { } aliasStatement ||
            body.Statements.FirstOrDefault(statement =>
                statement.Span.Contains(bindingSource.SpanStart)) is not { } sourceStatement ||
            aliasStatement.SpanStart >= sourceStatement.SpanStart ||
            !SyntaxMayReach(
                aliasSourceOperation.Syntax,
                bindingSourceOperation.Syntax,
                semanticModel) ||
            SequentialBooleanGuardsAreMutuallyExclusive(
                aliasSource,
                bindingSource,
                semanticModel) ||
            !ForLoopDefinitelyReachesSecondIteration(forStatement, semanticModel))
        {
            return false;
        }

        foreach (var statement in body.Statements)
        {
            if (statement.SpanStart > sourceStatement.SpanStart &&
                StatementDefinitelyPreventsNextIteration(
                    statement,
                    forStatement,
                    semanticModel))
            {
                return false;
            }
        }

        return !LoopBackEdgeMayReplaceLocal(
            forStatement,
            sourceLocal,
            bindingSource,
            aliasSource,
            semanticModel);
    }

    private static bool ForLoopDefinitelyReachesSecondIteration(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel)
    {
        if (forStatement.Declaration is not { Variables.Count: 1 } declaration ||
            declaration.Variables[0] is not { Initializer.Value: { } initializer } variable ||
            semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol loopLocal ||
            !TryGetIntegralConstant(initializer, semanticModel, out var initialValue) ||
            forStatement.Condition is not BinaryExpressionSyntax condition ||
            !TryGetForLoopUnitStep(
                forStatement,
                loopLocal,
                semanticModel,
                out var step,
                out var stepSyntax) ||
            !TryGetIntegralBounds(loopLocal.Type, out var minimum, out var maximum) ||
            initialValue < minimum ||
            initialValue > maximum ||
            HasAnySyntacticLoopLocalWrite(
                forStatement,
                loopLocal,
                stepSyntax,
                semanticModel) ||
            !EvaluateIntegralCondition(
                condition,
                loopLocal,
                initialValue,
                semanticModel,
                out var initiallyTrue) ||
            !initiallyTrue)
        {
            return false;
        }

        var wraps = initialValue == maximum && step > 0 ||
                    initialValue == minimum && step < 0;
        if (wraps &&
            semanticModel.GetOperation(stepSyntax) is
                IIncrementOrDecrementOperation { IsChecked: true })
        {
            return false;
        }

        var nextValue = wraps
            ? step > 0 ? minimum : maximum
            : initialValue + step;
        return EvaluateIntegralCondition(
                   condition,
                   loopLocal,
                   nextValue,
                   semanticModel,
                   out var trueAfterFirstIteration) &&
               trueAfterFirstIteration;
    }

    private static bool HasAnySyntacticLoopLocalWrite(
        ForStatementSyntax forStatement,
        ILocalSymbol loopLocal,
        ExpressionSyntax stepSyntax,
        SemanticModel semanticModel)
    {
        var loopOperation = semanticModel.GetOperation(forStatement);
        var nodes = forStatement.Statement.DescendantNodesAndSelf()
            .Concat(forStatement.Incrementors.SelectMany(incrementor =>
                incrementor.DescendantNodesAndSelf()));
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, stepSyntax))
                continue;

            var nodeOperation = semanticModel.GetOperation(node);
            var nestedExecutable = node.Ancestors().FirstOrDefault(syntax =>
                syntax is LocalFunctionStatementSyntax or
                    LambdaExpressionSyntax or
                    AnonymousMethodExpressionSyntax);
            if (loopOperation != null &&
                nodeOperation != null &&
                !loopOperation.SharesOwningExecutableRoot(nodeOperation) &&
                nestedExecutable is LocalFunctionStatementSyntax localFunction &&
                !LocalFunctionMayBeInvokedInLoop(
                    localFunction,
                    forStatement,
                    semanticModel))
            {
                continue;
            }

            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                    postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                ArgumentSyntax argument when
                    argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                    argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) => argument.Expression,
                _ => null,
            };
            if (target != null &&
                target.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Any(expression =>
                    semanticModel.GetOperation(expression)?.UnwrapConversions() is
                        ILocalReferenceOperation localReference &&
                    SymbolEqualityComparer.Default.Equals(localReference.Local, loopLocal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalFunctionMayBeInvokedInLoop(
        LocalFunctionStatementSyntax localFunction,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        HashSet<SyntaxNode>? activeExecutables = null)
    {
        if (semanticModel.GetDeclaredSymbol(localFunction) is not { } functionSymbol)
            return false;

        activeExecutables ??= new HashSet<SyntaxNode>();
        if (!activeExecutables.Add(localFunction))
            return false;

        try
        {
            var invocations = forStatement.Statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Concat(forStatement.Incrementors.SelectMany(incrementor =>
                    incrementor.DescendantNodesAndSelf()
                        .OfType<InvocationExpressionSyntax>()));
            foreach (var invocation in invocations)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(invocation).Symbol,
                        functionSymbol) ||
                    !InvocationMayRunWithinOwningExecutable(
                        invocation,
                        forStatement,
                        semanticModel))
                {
                    continue;
                }

                var enclosingExecutable = invocation.Ancestors().FirstOrDefault(syntax =>
                    syntax is LocalFunctionStatementSyntax or
                        LambdaExpressionSyntax or
                        AnonymousMethodExpressionSyntax);
                if (enclosingExecutable == null)
                    return true;
                if (enclosingExecutable is LocalFunctionStatementSyntax enclosingFunction &&
                    LocalFunctionMayBeInvokedInLoop(
                        enclosingFunction,
                        forStatement,
                        semanticModel,
                        activeExecutables))
                {
                    return true;
                }

                if (enclosingExecutable is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax &&
                    DirectDelegateInvocationMayRunInLoop(
                        enclosingExecutable,
                        forStatement,
                        semanticModel,
                        activeExecutables))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            activeExecutables.Remove(localFunction);
        }
    }

    private static bool DirectDelegateInvocationMayRunInLoop(
        SyntaxNode nestedExecutable,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        HashSet<SyntaxNode> activeExecutables)
    {
        if (!activeExecutables.Add(nestedExecutable))
            return false;

        try
        {
            if (GetDirectDelegateInvocation(nestedExecutable, semanticModel) is not { } invocation ||
                !InvocationMayRunWithinOwningExecutable(
                    invocation,
                    forStatement,
                    semanticModel))
            {
                return false;
            }

            var enclosingExecutable = invocation.Ancestors().FirstOrDefault(syntax =>
                syntax is LocalFunctionStatementSyntax or
                    LambdaExpressionSyntax or
                    AnonymousMethodExpressionSyntax);
            return enclosingExecutable switch
            {
                null => true,
                LocalFunctionStatementSyntax enclosingFunction =>
                    LocalFunctionMayBeInvokedInLoop(
                        enclosingFunction,
                        forStatement,
                        semanticModel,
                        activeExecutables),
                LambdaExpressionSyntax or AnonymousMethodExpressionSyntax =>
                    DirectDelegateInvocationMayRunInLoop(
                        enclosingExecutable,
                        forStatement,
                        semanticModel,
                        activeExecutables),
                _ => false,
            };
        }
        finally
        {
            activeExecutables.Remove(nestedExecutable);
        }
    }

    private static bool LoopBackEdgeMayReplaceLocal(
        ForStatementSyntax forStatement,
        ILocalSymbol local,
        SyntaxNode bindingSource,
        SyntaxNode aliasSource,
        SemanticModel semanticModel)
    {
        var nodes = forStatement.Statement.DescendantNodes()
            .Concat(forStatement.Incrementors.SelectMany(incrementor =>
                incrementor.DescendantNodesAndSelf()));
        foreach (var assignment in nodes.OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.Span == bindingSource.Span ||
                semanticModel.GetOperation(assignment.Left)?.UnwrapConversions() is not
                    ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            var inIncrementor = forStatement.Incrementors.Any(incrementor =>
                incrementor.Span.Contains(assignment.SpanStart));
            if (inIncrementor ||
                assignment.SpanStart > bindingSource.Span.End ||
                assignment.Span.End < aliasSource.SpanStart)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDelegateCreationPart(
        SyntaxNode syntax,
        SemanticModel semanticModel)
    {
        var creation = syntax switch
        {
            BaseObjectCreationExpressionSyntax directCreation => directCreation,
            ArgumentListSyntax { Parent: BaseObjectCreationExpressionSyntax parentCreation } =>
                parentCreation,
            ArgumentSyntax
            {
                Parent: ArgumentListSyntax
                {
                    Parent: BaseObjectCreationExpressionSyntax parentCreation,
                },
            } => parentCreation,
            _ => null,
        };
        return creation != null &&
               semanticModel.GetTypeInfo(creation).Type?.TypeKind == TypeKind.Delegate;
    }

    private readonly struct NestedExecutableBinding
    {
        public NestedExecutableBinding(ISymbol symbol, SyntaxNode? sourceSyntax)
        {
            Symbol = symbol;
            SourceSyntax = sourceSyntax;
        }

        public ISymbol Symbol { get; }

        public SyntaxNode? SourceSyntax { get; }
    }

    private static ISymbol? GetInvokedDelegateSymbol(
        InvocationExpressionSyntax invocation,
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel)
    {
        var directSymbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol;
        if (directSymbol is ILocalSymbol)
            return directSymbol;

        var conditionalAccess = invocation.AncestorsAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(access => access.WhenNotNull.Span.Contains(invocation.SpanStart));
        if (conditionalAccess != null &&
            semanticModel.GetOperation(conditionalAccess.Expression) is
                ILocalReferenceOperation conditionalLocal)
        {
            return conditionalLocal.Local;
        }

        return UnwrapTerminalThrowOperand(invocationOperation.Instance) is
            ILocalReferenceOperation localReference
                ? localReference.Local
                : null;
    }

    private static bool PotentialOperationCanReachCatch(
        IOperation operation,
        CatchClauseSyntax catchClause,
        TryStatementSyntax targetTry,
        SyntaxNode terminalThrow,
        IOperation mutation)
    {
        var semanticModel = operation.SemanticModel ?? mutation.SemanticModel;
        if (semanticModel == null) return true;

        var caughtType = catchClause.Declaration == null
            ? semanticModel.Compilation.GetTypeByMetadataName("System.Exception")
            : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        if (caughtType == null) return true;

        var exactTypes = new List<ITypeSymbol>();
        switch (operation)
        {
            case IFieldReferenceOperation { Field: { IsStatic: true, HasConstantValue: false } }:
                AddMetadataType(exactTypes, semanticModel, "System.TypeInitializationException");
                break;

            case IFieldReferenceOperation:
                AddMetadataType(exactTypes, semanticModel, "System.NullReferenceException");
                break;

            case IArrayElementReferenceOperation:
                AddMetadataType(exactTypes, semanticModel, "System.NullReferenceException");
                AddMetadataType(exactTypes, semanticModel, "System.IndexOutOfRangeException");
                break;

            case IObjectCreationOperation { Type: { } creationType } creation
                when IsTopLevelSystemExceptionConstruction(
                    creation, terminalThrow, semanticModel):
                AddExactType(exactTypes, creationType);
                break;

            default:
                AddExactType(exactTypes, caughtType);
                break;
        }

        return exactTypes.Any(candidate =>
            ExactExceptionReachesCatch(
                candidate, catchClause, targetTry, semanticModel) &&
            CanCatchExactType(candidate, caughtType, catchClause) &&
            ExactExceptionEscapesNestedTries(
                candidate, operation.Syntax, targetTry, semanticModel));
    }

    private static bool ExactExceptionReachesCatch(
        ITypeSymbol exactType,
        CatchClauseSyntax targetCatch,
        TryStatementSyntax targetTry,
        SemanticModel semanticModel)
    {
        foreach (var catchClause in targetTry.Catches)
        {
            if (ReferenceEquals(catchClause, targetCatch))
                return true;

            var caughtType = catchClause.Declaration == null
                ? semanticModel.Compilation.GetTypeByMetadataName("System.Exception")
                : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (!CanCatchExactType(exactType, caughtType, catchClause))
                continue;

            if (catchClause.Filter?.FilterExpression is not { } filterExpression)
                return false;

            var constant = semanticModel.GetConstantValue(filterExpression);
            if (constant is { HasValue: true, Value: true })
                return false;
        }

        return false;
    }

    private static bool IsTopLevelSystemExceptionConstruction(
        IObjectCreationOperation creation,
        SyntaxNode terminalThrow,
        SemanticModel semanticModel)
    {
        var exceptionType = semanticModel.Compilation
            .GetTypeByMetadataName("System.Exception");
        var terminalOperand = UnwrapTerminalThrowOperand(
            semanticModel.GetOperation(terminalThrow) is IThrowOperation throwOperation
                ? throwOperation.Exception
                : null);
        var namespaceName = creation.Type?.ContainingNamespace?.ToDisplayString();
        return creation.Type != null &&
               exceptionType != null &&
               terminalOperand?.Syntax.Span == creation.Syntax.Span &&
               (namespaceName == "System" ||
                namespaceName?.StartsWith(
                    "System.", System.StringComparison.Ordinal) == true) &&
               SymbolEqualityComparer.Default.Equals(
                   creation.Type.ContainingAssembly,
                   exceptionType.ContainingAssembly) &&
               IsSameOrDerivedFrom(creation.Type, exceptionType);
    }

    private static IOperation? UnwrapTerminalThrowOperand(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { OperatorMethod: null } conversion:
                    operation = conversion.Operand;
                    continue;

                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;

                default:
                    return operation;
            }
        }
    }

    private static void AddMetadataType(
        List<ITypeSymbol> types,
        SemanticModel semanticModel,
        string metadataName)
    {
        if (semanticModel.Compilation.GetTypeByMetadataName(metadataName) is { } type)
            AddExactType(types, type);
    }

    private static bool MemberPathIsPrefix(
        ImmutableArray<MemberPathSegment> candidatePrefix,
        ImmutableArray<MemberPathSegment> path)
    {
        if (candidatePrefix.Length > path.Length) return false;

        for (var i = 0; i < candidatePrefix.Length; i++)
        {
            if (!MemberPathSymbolsAreEquivalent(candidatePrefix[i], path[i]))
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

    private static bool MemberPathSymbolsAreEquivalent(
        MemberPathSegment left,
        MemberPathSegment right)
    {
        if (SymbolEqualityComparer.Default.Equals(left.Member, right.Member))
            return true;

        if (left.Member is not IPropertySymbol leftProperty ||
            right.Member is not IPropertySymbol rightProperty)
        {
            return false;
        }

        var resolvedLeft = ResolveInterfaceProperty(
            leftProperty, left.ReceiverType);
        var resolvedRight = ResolveInterfaceProperty(
            rightProperty, right.ReceiverType);
        return SymbolEqualityComparer.Default.Equals(
            GetBaseProperty(resolvedLeft),
            GetBaseProperty(resolvedRight));
    }

    private static IPropertySymbol GetBaseProperty(IPropertySymbol property)
    {
        while (property.OverriddenProperty is { } overriddenProperty)
            property = overriddenProperty;

        return property;
    }

    private static IPropertySymbol ResolveInterfaceProperty(
        IPropertySymbol property,
        ITypeSymbol? receiverType)
    {
        if (property.ContainingType.TypeKind != TypeKind.Interface ||
            receiverType is not INamedTypeSymbol effectiveReceiverType ||
            effectiveReceiverType.TypeKind == TypeKind.Interface)
        {
            return property;
        }

        return effectiveReceiverType.FindImplementationForInterfaceMember(property) is
            IPropertySymbol resolvedImplementation
                ? resolvedImplementation
                : property;
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
