using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private static bool IsSaveInsideCatchGuardedRetryAttempt(IInvocationOperation invocation, ILoopOperation loop)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax)
            return false;

        var tryStatement = FindTryStatementBetweenInvocationAndLoop(invocationSyntax, loop.Syntax);
        return tryStatement != null &&
               tryStatement.Catches.Count > 0 &&
               tryStatement.Block.Span.Contains(invocationSyntax.SpanStart) &&
               HasLoopExitAfterSave(tryStatement.Block, invocationSyntax);
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

    private static bool HasLoopExitAfterSave(BlockSyntax tryBlock, InvocationExpressionSyntax invocationSyntax)
    {
        var containingStatement = invocationSyntax
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent == tryBlock);

        if (containingStatement == null)
            return false;

        var statementIndex = tryBlock.Statements.IndexOf(containingStatement);
        if (statementIndex < 0)
            return false;

        return tryBlock.Statements
            .Skip(statementIndex + 1)
            .Any(statement => statement is BreakStatementSyntax or ReturnStatementSyntax);
    }
}
