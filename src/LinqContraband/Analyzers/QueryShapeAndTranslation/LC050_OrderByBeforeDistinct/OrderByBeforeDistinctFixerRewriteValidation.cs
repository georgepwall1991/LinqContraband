using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC050_OrderByBeforeDistinct;

public sealed partial class OrderByBeforeDistinctFixer
{
    private static bool TryGetSimpleLambda(ExpressionSyntax expression, out string parameter, out ExpressionSyntax body)
    {
        parameter = string.Empty;
        body = null!;

        switch (expression)
        {
            case SimpleLambdaExpressionSyntax { ExpressionBody: { } simpleBody } simple:
                parameter = simple.Parameter.Identifier.ValueText;
                body = simpleBody;
                return true;
            case ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } parenthesizedBody, ParameterList.Parameters.Count: 1 } parenthesized
                when parenthesized.ParameterList.Parameters[0].Type == null:
                parameter = parenthesized.ParameterList.Parameters[0].Identifier.ValueText;
                body = parenthesizedBody;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// True when the sort key and the projection are the same expression once the lambda parameters are aligned,
    /// for example <c>o =&gt; o.Customer.Name</c> and <c>c =&gt; c.Customer.Name</c>.
    /// </summary>
    private static bool AreSameKey(string orderParameter, ExpressionSyntax orderBody, string selectParameter, ExpressionSyntax selectBody)
    {
        if (orderBody.DescendantNodesAndSelf().Any(node => node is AnonymousFunctionExpressionSyntax) ||
            selectBody.DescendantNodesAndSelf().Any(node => node is AnonymousFunctionExpressionSyntax))
        {
            return false;
        }

        var renamed = orderParameter == selectParameter
            ? orderBody
            : orderBody.ReplaceNodes(
                orderBody.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                    .Where(identifier => identifier.Identifier.ValueText == orderParameter && !IsMemberName(identifier)),
                (original, _) => SyntaxFactory.IdentifierName(selectParameter).WithTriviaFrom(original));

        return SyntaxFactory.AreEquivalent(renamed, selectBody, topLevel: false);
    }

    private static bool IsMemberName(IdentifierNameSyntax identifier)
    {
        return identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier;
    }

    /// <summary>
    /// The rewrite ends in OrderBy, so the expression's type becomes IOrderedQueryable&lt;T&gt;. That only matters
    /// when it initializes a <c>var</c> local that is later reassigned with a plain IQueryable&lt;T&gt;.
    /// </summary>
    private static bool ResultTypeChangeIsSafe(
        InvocationExpressionSyntax distinct,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SyntaxNode current = distinct;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
            current = parenthesized;

        if (current.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } ||
            declarator.Parent is not VariableDeclarationSyntax { Type.IsVar: true })
        {
            return true;
        }

        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local)
            return false;

        var scope = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? (SyntaxNode)declarator.SyntaxTree.GetRoot(cancellationToken);
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != local.Name) continue;
            if (!IsWrite(identifier)) continue;
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
                return false;
        }

        return true;
    }

    private static bool IsWrite(IdentifierNameSyntax identifier)
    {
        return identifier.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == identifier,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None) || argument.Parent is TupleExpressionSyntax,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => true,
            _ => false
        };
    }
}
