using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    /// <summary>
    /// The loop's body runs at most once, so work inside it is not repeated per item:
    /// <list type="bullet">
    /// <item>a <c>foreach</c> over a LanguageExt <c>Option&lt;T&gt;</c>, which holds zero or one value
    /// (<c>foreach (var user in maybeUser) { ... }</c> is that library's idiom for "if present");</item>
    /// <item>a loop whose body ends in a top-level <c>return</c>, <c>throw</c>, <c>break</c> or <c>yield break</c>
    /// and contains no <c>continue</c> or <c>goto</c> that could start another pass.</item>
    /// </list>
    /// </summary>
    public static bool RunsAtMostOnce(this ILoopOperation loop)
    {
        if (loop is IForEachLoopOperation forEach && IsZeroOrOneValueContainer(forEach.Collection.UnwrapConversions().Type))
            return true;

        var body = loop.Syntax switch
        {
            CommonForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null
        };

        if (body is not BlockSyntax { Statements.Count: > 0 } block || !ExitsLoop(block.Statements[block.Statements.Count - 1]))
            return false;

        foreach (var node in block.DescendantNodes(descendIntoChildren: node => !IsNestedFunction(node)))
        {
            if (node is GotoStatementSyntax)
                return false;

            if (node is ContinueStatementSyntax continueStatement && ReferenceEquals(NearestLoop(continueStatement), loop.Syntax))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Every path that runs <paramref name="operation"/> then leaves <paramref name="loop"/> before another pass:
    /// the operation sits in a <c>return</c> or <c>throw</c>, or a later statement of a block it is in is a
    /// <c>return</c>, <c>throw</c>, <c>yield break</c> or a <c>break</c> out of this loop, with no <c>continue</c> or
    /// <c>goto</c> in between (<c>if (match) { await db.SaveChangesAsync(); return; }</c>). A statement that holds the
    /// operation and can itself <c>continue</c> (<c>if (db.SaveChanges() &gt; 0) continue; break;</c>) and a <c>throw</c>
    /// that a <c>catch</c> inside the loop may handle do not count as leaving.
    /// </summary>
    public static bool LeavesLoopAfter(this IOperation operation, ILoopOperation loop)
    {
        var loopSyntax = loop.Syntax;
        var loopBody = loopSyntax switch
        {
            CommonForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null
        };

        if (loopBody == null || !loopBody.Span.Contains(operation.Syntax.Span))
            return false;

        for (SyntaxNode? current = operation.Syntax; current != null && current != loopSyntax; current = current.Parent)
        {
            if (IsNestedFunction(current))
                return false;

            if (current is ReturnStatementSyntax)
                return true;

            if (current is ThrowStatementSyntax)
                return !MayBeCaughtInsideLoop(current, loopSyntax);

            // A catch or finally block runs on a path the try body may not have taken; stop looking there.
            if (current is CatchClauseSyntax or FinallyClauseSyntax)
                return false;

            if (current is StatementSyntax statement && current.Parent is BlockSyntax block)
            {
                // The statement holding the operation may branch back to the loop before reaching a later exit.
                if (CanRestartOrJump(statement, loopSyntax))
                    return false;

                var index = block.Statements.IndexOf(statement);
                for (var next = index + 1; next < block.Statements.Count; next++)
                {
                    var following = block.Statements[next];
                    if (IsExitOf(following, loopSyntax))
                        return true;

                    if (CanRestartOrJump(following, loopSyntax))
                        return false;
                }
            }
        }

        return false;
    }

    private static bool IsExitOf(StatementSyntax statement, SyntaxNode loopSyntax)
    {
        if (statement is ReturnStatementSyntax)
            return true;

        if (statement is ThrowStatementSyntax)
            return !MayBeCaughtInsideLoop(statement, loopSyntax);

        if (statement is YieldStatementSyntax yieldStatement)
            return yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);

        return statement is BreakStatementSyntax && ReferenceEquals(NearestBreakTarget(statement), loopSyntax);
    }

    /// <summary>A <c>try</c> with a <c>catch</c> between the throw and the loop may handle it and start another pass.</summary>
    private static bool MayBeCaughtInsideLoop(SyntaxNode throwStatement, SyntaxNode loopSyntax)
    {
        for (SyntaxNode? child = throwStatement, current = throwStatement.Parent;
             current != null && current != loopSyntax;
             child = current, current = current.Parent)
        {
            if (current is TryStatementSyntax { Catches.Count: > 0 } tryStatement && child == tryStatement.Block)
                return true;
        }

        return false;
    }

    private static bool CanRestartOrJump(StatementSyntax statement, SyntaxNode loopSyntax)
    {
        foreach (var node in statement.DescendantNodesAndSelf(descendIntoChildren: node => !IsNestedFunction(node)))
        {
            if (node is GotoStatementSyntax)
                return true;

            if (node is ContinueStatementSyntax continueStatement && ReferenceEquals(NearestLoop(continueStatement), loopSyntax))
                return true;
        }

        return false;
    }

    private static SyntaxNode? NearestBreakTarget(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax)
                return current;

            if (IsNestedFunction(current))
                return null;
        }

        return null;
    }

    private static bool ExitsLoop(StatementSyntax statement)
    {
        return statement is ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax ||
               statement is YieldStatementSyntax yieldStatement && yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);
    }

    private static SyntaxNode? NearestLoop(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                return current;

            if (IsNestedFunction(current))
                return null;
        }

        return null;
    }

    private static bool IsNestedFunction(SyntaxNode node)
    {
        return node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
    }

    private static bool IsZeroOrOneValueContainer(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Arity: 1, Name: "Option" or "OptionUnsafe" } named &&
               named.ContainingNamespace is { Name: "LanguageExt", ContainingNamespace.IsGlobalNamespace: true };
    }
}
