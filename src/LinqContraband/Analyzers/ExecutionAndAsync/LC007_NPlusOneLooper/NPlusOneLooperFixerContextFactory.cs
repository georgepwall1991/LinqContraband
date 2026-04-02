using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

public sealed partial class NPlusOneLooperFixer
{
    private static async Task<ExplicitLoadFixContext?> TryCreateFixContextAsync(
        Document document,
        InvocationExpressionSyntax loadInvocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return null;

        if (!TryGetDirectLoadStatement(loadInvocation, out var loadStatement))
            return null;

        var loop = loadStatement.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault();
        if (loop == null)
            return null;

        if (!IsSafeDirectSingleLoadLoop(loop, loadStatement))
            return null;

        var loopVariableName = loop.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(loopVariableName))
            return null;

        if (!TryGetNavigationLambda(loadInvocation, loopVariableName, out var navigationLambda))
            return null;

        if (!TryResolveQuerySourceTarget(loop.Expression, semanticModel, cancellationToken, out var queryTargetNode, out var querySourceExpression))
            return null;

        if (!TryAddInclude(querySourceExpression, navigationLambda, semanticModel, cancellationToken, out var rewrittenQuerySource))
            return null;

        return new ExplicitLoadFixContext(loadStatement, queryTargetNode, rewrittenQuerySource);
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        return root.FindNode(span) as InvocationExpressionSyntax
               ?? root.FindToken(span.Start).Parent?.AncestorsAndSelf()
                   .OfType<InvocationExpressionSyntax>()
                   .FirstOrDefault();
    }

    private static bool IsSafeDirectSingleLoadLoop(ForEachStatementSyntax loop, StatementSyntax loadStatement)
    {
        return IsDirectLoopStatement(loop, loadStatement) &&
               !ContainsUnsafeControlFlow(loop.Statement) &&
               CountExplicitLoads(loop.Statement) == 1;
    }

    private sealed class ExplicitLoadFixContext
    {
        public ExplicitLoadFixContext(
            ExpressionStatementSyntax loadStatement,
            ExpressionSyntax queryTargetNode,
            ExpressionSyntax rewrittenQuerySource)
        {
            LoadStatement = loadStatement;
            QueryTargetNode = queryTargetNode;
            RewrittenQuerySource = rewrittenQuerySource;
        }

        public ExpressionStatementSyntax LoadStatement { get; }
        public ExpressionSyntax QueryTargetNode { get; }
        public ExpressionSyntax RewrittenQuerySource { get; }
    }
}
