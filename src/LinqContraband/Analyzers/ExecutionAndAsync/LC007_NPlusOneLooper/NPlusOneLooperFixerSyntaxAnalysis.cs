using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

public sealed partial class NPlusOneLooperFixer
{
    private static bool TryGetDirectLoadStatement(
        InvocationExpressionSyntax invocation,
        out ExpressionStatementSyntax loadStatement)
    {
        loadStatement = invocation.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault()!;
        return loadStatement != null;
    }

    private static bool IsDirectLoopStatement(ForEachStatementSyntax loop, StatementSyntax statement)
    {
        if (loop.Statement is BlockSyntax block)
            return block.Statements.Contains(statement);

        return loop.Statement == statement;
    }

    private static bool ContainsUnsafeControlFlow(StatementSyntax loopBody)
    {
        return loopBody.DescendantNodes().Any(node =>
            node is IfStatementSyntax or SwitchStatementSyntax or ReturnStatementSyntax or BreakStatementSyntax or
                ContinueStatementSyntax or ThrowStatementSyntax or TryStatementSyntax or GotoStatementSyntax or
                YieldStatementSyntax);
    }

    private static int CountExplicitLoads(StatementSyntax loopBody)
    {
        return loopBody.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(IsExplicitLoadInvocation);
    }

    private static bool IsExplicitLoadInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text is "Load" or "LoadAsync";
    }

    private static bool TryGetNavigationLambda(
        InvocationExpressionSyntax loadInvocation,
        string loopVariableName,
        out LambdaExpressionSyntax navigationLambda)
    {
        navigationLambda = null!;

        if (loadInvocation.Expression is not MemberAccessExpressionSyntax loadMember ||
            loadMember.Expression is not InvocationExpressionSyntax accessInvocation ||
            accessInvocation.Expression is not MemberAccessExpressionSyntax accessMember ||
            accessMember.Expression is not InvocationExpressionSyntax entryInvocation ||
            entryInvocation.Expression is not MemberAccessExpressionSyntax entryMember)
        {
            return false;
        }

        if (accessMember.Name.Identifier.Text is not ("Collection" or "Reference"))
            return false;

        if (entryMember.Name.Identifier.Text != "Entry" || entryInvocation.ArgumentList.Arguments.Count != 1)
            return false;

        if (entryInvocation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax identifier)
            return false;

        if (identifier.Identifier.ValueText != loopVariableName)
            return false;

        if (accessInvocation.ArgumentList.Arguments.Count != 1 ||
            accessInvocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        navigationLambda = lambda;
        return true;
    }

    private static bool TryResolveQuerySourceTarget(
        ExpressionSyntax loopExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax queryTargetNode,
        out ExpressionSyntax querySourceExpression)
    {
        queryTargetNode = null!;
        querySourceExpression = null!;

        if (loopExpression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local &&
            TryGetLocalInitializerExpression(local, semanticModel, cancellationToken, out var initializerExpression))
        {
            queryTargetNode = initializerExpression;
            querySourceExpression = initializerExpression;
            return true;
        }

        queryTargetNode = loopExpression;
        querySourceExpression = loopExpression;
        return true;
    }

    private static bool TryGetLocalInitializerExpression(
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializerExpression)
    {
        initializerExpression = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var executableRoot = declarator.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (executableRoot == null)
            return false;

        foreach (var assignment in executableRoot.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Span.Contains(declarator.Span))
                continue;

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, local))
                return false;
        }

        initializerExpression = declarator.Initializer.Value;
        return true;
    }

    private static bool TryAddInclude(
        ExpressionSyntax querySourceExpression,
        LambdaExpressionSyntax navigationLambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenExpression)
    {
        rewrittenExpression = null!;

        if (querySourceExpression is not InvocationExpressionSyntax terminalInvocation ||
            terminalInvocation.Expression is not MemberAccessExpressionSyntax terminalMember)
        {
            return false;
        }

        var source = terminalMember.Expression;
        if (semanticModel.GetTypeInfo(source, cancellationToken).Type?.IsIQueryable() != true)
            return false;

        var includeMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source.WithoutTrivia(),
            SyntaxFactory.IdentifierName("Include"));

        var includeInvocation = SyntaxFactory.InvocationExpression(
                includeMember,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(navigationLambda.WithoutTrivia()))))
            .WithTriviaFrom(source);

        rewrittenExpression = terminalInvocation
            .WithExpression(terminalMember.WithExpression(includeInvocation))
            .WithTriviaFrom(querySourceExpression);

        return true;
    }
}
