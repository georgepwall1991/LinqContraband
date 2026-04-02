using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionFixer
{
    private static async Task<Document> AddSelectProjectionAsync(
        Document document,
        ProjectionFixContext fixContext,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (fixContext.Invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var sourceExpression = memberAccess.Expression;
        var paramName = "e";

        var propertyAssignments = fixContext.AccessedProperties
            .OrderBy(p => p)
            .Select(p => CreateAnonymousObjectMemberDeclarator(paramName, p))
            .ToArray();

        ExpressionSyntax lambdaBody = SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(propertyAssignments));

        var lambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName)),
            lambdaBody);

        var selectInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                sourceExpression,
                SyntaxFactory.IdentifierName("Select")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(lambda))));

        editor.ReplaceNode(sourceExpression, selectInvocation);
        editor.EnsureUsing("System.Linq");
        return editor.GetChangedDocument();
    }

    private static AnonymousObjectMemberDeclaratorSyntax CreateAnonymousObjectMemberDeclarator(string paramName, string propertyName)
    {
        return SyntaxFactory.AnonymousObjectMemberDeclarator(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(paramName),
                SyntaxFactory.IdentifierName(propertyName)));
    }
}
