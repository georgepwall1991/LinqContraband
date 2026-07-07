using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering;

public sealed partial class AsyncEnumerableBufferingFixer
{
    private static bool TryGetContainingLoopAndDeclaration(
        InvocationExpressionSyntax invocation,
        out LocalDeclarationStatementSyntax localDeclaration,
        out ForEachStatementSyntax loopSyntax)
    {
        localDeclaration = null!;
        loopSyntax = null!;

        if (invocation.AncestorsAndSelf().OfType<AwaitExpressionSyntax>().FirstOrDefault() is not AwaitExpressionSyntax awaitExpression)
            return false;

        if (awaitExpression.Parent is not EqualsValueClauseSyntax equalsValueClause)
            return false;

        if (equalsValueClause.Parent is not VariableDeclaratorSyntax declarator)
            return false;

        if (declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declaration)
            return false;

        if (declarator.Parent?.Parent?.Parent is not BlockSyntax block)
            return false;

        var statements = block.Statements;
        var declarationIndex = -1;
        for (var i = 0; i < statements.Count; i++)
        {
            if (ReferenceEquals(statements[i], declaration))
            {
                declarationIndex = i;
                break;
            }
        }

        if (declarationIndex < 0 || declarationIndex + 1 >= statements.Count)
            return false;

        if (statements[declarationIndex + 1] is not ForEachStatementSyntax loop)
            return false;

        localDeclaration = declaration;
        loopSyntax = loop;
        return true;
    }
}
