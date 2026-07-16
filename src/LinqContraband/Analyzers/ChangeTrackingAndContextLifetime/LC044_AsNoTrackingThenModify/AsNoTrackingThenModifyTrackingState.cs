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
        int beforeSpanStart)
    {
        var nestedExecutable = nestedOperation.Syntax.Ancestors().FirstOrDefault(syntax =>
            syntax is LocalFunctionStatementSyntax or
                LambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax);
        ISymbol? invokedSymbol = nestedExecutable switch
        {
            LocalFunctionStatementSyntax localFunction =>
                semanticModel.GetDeclaredSymbol(localFunction),
            LambdaExpressionSyntax or AnonymousMethodExpressionSyntax =>
                nestedExecutable.Ancestors().OfType<VariableDeclaratorSyntax>()
                    .Select(declarator => semanticModel.GetDeclaredSymbol(declarator))
                    .FirstOrDefault(symbol => symbol != null),
            _ => null,
        };
        if (invokedSymbol == null)
            return false;

        foreach (var invocation in executableRoot.Syntax.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            if (invocation.SpanStart >= beforeSpanStart ||
                semanticModel.GetOperation(invocation) is not IInvocationOperation invocationOperation ||
                !executableRoot.SharesOwningExecutableRoot(invocationOperation))
            {
                continue;
            }

            var candidate = invokedSymbol is IMethodSymbol
                ? semanticModel.GetSymbolInfo(invocation).Symbol
                : GetInvokedDelegateSymbol(invocation, invocationOperation, semanticModel);
            if (SymbolEqualityComparer.Default.Equals(candidate, invokedSymbol))
                return true;
        }

        return false;
    }

    private static ISymbol? GetInvokedDelegateSymbol(
        InvocationExpressionSyntax invocation,
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel)
    {
        var directSymbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol;
        if (directSymbol is ILocalSymbol)
            return directSymbol;

        return UnwrapTerminalThrowOperand(invocationOperation.Instance) is
            ILocalReferenceOperation localReference
                ? localReference.Local
                : null;
    }

    private static bool PotentialOperationCanReachCatch(
        IOperation operation,
        CatchClauseSyntax catchClause,
        TryStatementSyntax targetTry,
        ThrowStatementSyntax terminalThrow,
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
        ThrowStatementSyntax terminalThrow,
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
