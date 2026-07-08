using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private enum RetrySuccessExitKind
    {
        None,
        Break,
        Return,
    }

    private static bool IsSaveInsideCatchGuardedRetryAttempt(IInvocationOperation invocation, ILoopOperation loop)
    {
        return IsOperationInsideCatchGuardedRetryAttempt(invocation, loop);
    }

    private static bool IsOperationInsideCatchGuardedRetryAttempt(IOperation operation, ILoopOperation loop)
    {
        return GetCatchGuardedRetrySuccessExitKind(operation, loop) != RetrySuccessExitKind.None;
    }

    private static RetrySuccessExitKind GetCatchGuardedRetrySuccessExitKind(IOperation operation, ILoopOperation loop)
    {
        if (operation.Syntax is not InvocationExpressionSyntax invocationSyntax)
            return RetrySuccessExitKind.None;

        var tryStatement = FindTryStatementBetweenInvocationAndLoop(invocationSyntax, loop.Syntax);
        if (tryStatement == null ||
            tryStatement.Catches.Count == 0 ||
            !tryStatement.Block.Span.Contains(invocationSyntax.SpanStart))
        {
            return RetrySuccessExitKind.None;
        }

        return GetLoopExitAfterSave(tryStatement.Block, invocationSyntax, loop.Syntax);
    }

    private static TryStatementSyntax? FindTryStatementBetweenInvocationAndLoop(
        InvocationExpressionSyntax invocationSyntax,
        SyntaxNode loopSyntax)
    {
        foreach (var ancestor in invocationSyntax.Ancestors())
        {
            if (ancestor == loopSyntax)
                return null;

            if (ancestor is TryStatementSyntax tryStatement)
                return tryStatement;
        }

        return null;
    }

    private static bool HasLoopExitAfterSave(
        BlockSyntax tryBlock,
        InvocationExpressionSyntax invocationSyntax,
        SyntaxNode loopSyntax)
    {
        return GetLoopExitAfterSave(tryBlock, invocationSyntax, loopSyntax) != RetrySuccessExitKind.None;
    }

    private static RetrySuccessExitKind GetLoopExitAfterSave(
        BlockSyntax tryBlock,
        InvocationExpressionSyntax invocationSyntax,
        SyntaxNode loopSyntax)
    {
        var containingStatement = invocationSyntax
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent == tryBlock);

        if (containingStatement == null)
            return RetrySuccessExitKind.None;

        var statementIndex = tryBlock.Statements.IndexOf(containingStatement);
        if (statementIndex < 0)
            return RetrySuccessExitKind.None;

        foreach (var statement in tryBlock.Statements.Skip(statementIndex + 1))
        {
            if (statement is BreakStatementSyntax breakStatement &&
                BreakTargetsLoop(breakStatement, loopSyntax))
            {
                return RetrySuccessExitKind.Break;
            }

            if (statement is ReturnStatementSyntax)
                return RetrySuccessExitKind.Return;
        }

        return RetrySuccessExitKind.None;
    }

    private static bool BreakTargetsLoop(BreakStatementSyntax breakStatement, SyntaxNode loopSyntax)
    {
        foreach (var ancestor in breakStatement.Ancestors())
        {
            if (ancestor == loopSyntax)
                return true;

            if (ancestor is ForStatementSyntax
                or ForEachStatementSyntax
                or ForEachVariableStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or SwitchStatementSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
