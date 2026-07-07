using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopFixer
{
    private static bool TryGetMovableSaveStatement(
        InvocationExpressionSyntax invocation,
        out ExpressionStatementSyntax expressionStatement,
        out StatementSyntax loop)
    {
        var statement = invocation.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault();
        if (statement == null)
        {
            var awaitExpr = invocation.AncestorsAndSelf().OfType<AwaitExpressionSyntax>().FirstOrDefault();
            statement = awaitExpr?.Parent as ExpressionStatementSyntax;
        }

        if (statement == null)
        {
            expressionStatement = null!;
            loop = null!;
            return false;
        }

        var containingLoop = statement.Ancestors().FirstOrDefault(n =>
            n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax) as StatementSyntax;

        if (containingLoop == null)
        {
            expressionStatement = null!;
            loop = null!;
            return false;
        }

        if (containingLoop is not DoStatementSyntax)
        {
            expressionStatement = null!;
            loop = null!;
            return false;
        }

        if (containingLoop.Ancestors().Any(static n =>
                n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax))
        {
            expressionStatement = null!;
            loop = null!;
            return false;
        }

        expressionStatement = statement;
        loop = containingLoop;

        if (ContainsUnsafeControlFlow(loop)) return false;
        if (!IsDirectTerminalStatement(expressionStatement, loop)) return false;
        if (CountSaveStatements(loop) != 1) return false;

        return true;
    }

    private static bool ContainsUnsafeControlFlow(StatementSyntax loop)
    {
        return loop.DescendantNodes().Any(node =>
            node is BreakStatementSyntax or ContinueStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or
                YieldStatementSyntax or TryStatementSyntax or GotoStatementSyntax);
    }

    private static bool IsDirectTerminalStatement(ExpressionStatementSyntax statement, StatementSyntax loop)
    {
        if (loop switch
        {
            ForStatementSyntax forLoop => forLoop.Statement,
            ForEachStatementSyntax forEachLoop => forEachLoop.Statement,
            WhileStatementSyntax whileLoop => whileLoop.Statement,
            DoStatementSyntax doLoop => doLoop.Statement,
            _ => null
        } is not StatementSyntax body)
        {
            return false;
        }

        if (body is BlockSyntax block)
        {
            return block.Statements.LastOrDefault() == statement;
        }

        return body == statement;
    }

    private static int CountSaveStatements(StatementSyntax loop)
    {
        return loop.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text is "SaveChanges" or "SaveChangesAsync");
    }

    private static bool IsSaveReceiverDeclaredInsideLoop(
        InvocationExpressionSyntax invocation,
        StatementSyntax loop,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        if (receiverSymbol == null)
            return true;

        foreach (var syntaxReference in receiverSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            if (loop.Span.Contains(syntax.SpanStart) && syntax.Span.End <= loop.Span.End)
                return true;
        }

        return false;
    }
}
