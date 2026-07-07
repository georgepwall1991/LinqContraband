using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering;

public sealed partial class AsyncEnumerableBufferingAnalyzer
{
    private static readonly ImmutableHashSet<string> BufferMethods = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "ToListAsync",
        "ToArrayAsync");

    internal static bool TryGetImmediateBufferedLocal(
        ForEachStatementSyntax loopSyntax,
        ILocalSymbol local,
        out BufferInfo bufferInfo)
    {
        bufferInfo = null!;

        if (loopSyntax.Parent is not BlockSyntax block)
            return false;

        var statements = block.Statements;
        var loopIndex = -1;
        for (var i = 0; i < statements.Count; i++)
        {
            if (ReferenceEquals(statements[i], loopSyntax))
            {
                loopIndex = i;
                break;
            }
        }

        if (loopIndex <= 0)
            return false;

        if (statements[loopIndex - 1] is not LocalDeclarationStatementSyntax localDeclaration)
            return false;

        if (localDeclaration.Declaration.Variables.Count != 1)
            return false;

        var declarator = localDeclaration.Declaration.Variables[0];
        if (declarator.Identifier.ValueText != local.Name)
            return false;

        if (declarator.Initializer?.Value is not AwaitExpressionSyntax awaitExpression)
            return false;

        if (awaitExpression.Expression is not InvocationExpressionSyntax invocationSyntax)
            return false;

        if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (invocationSyntax.ArgumentList.Arguments.Count != 0)
            return false;

        if (!BufferMethods.Contains(memberAccess.Name.Identifier.ValueText))
            return false;

        bufferInfo = new BufferInfo(localDeclaration, loopSyntax, invocationSyntax, memberAccess.Expression, memberAccess.Name.Identifier.ValueText);
        return true;
    }

    internal sealed class BufferInfo
    {
        public BufferInfo(
            LocalDeclarationStatementSyntax localDeclaration,
            ForEachStatementSyntax loopSyntax,
            InvocationExpressionSyntax bufferInvocation,
            ExpressionSyntax sourceExpression,
            string bufferMethodName)
        {
            LocalDeclaration = localDeclaration;
            LoopSyntax = loopSyntax;
            BufferInvocation = bufferInvocation;
            SourceExpression = sourceExpression;
            BufferMethodName = bufferMethodName;
        }

        public LocalDeclarationStatementSyntax LocalDeclaration { get; }

        public ForEachStatementSyntax LoopSyntax { get; }

        public InvocationExpressionSyntax BufferInvocation { get; }

        public ExpressionSyntax SourceExpression { get; }

        public string BufferMethodName { get; }
    }
}
