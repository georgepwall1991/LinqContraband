using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesFixer
{
    private static bool TryRewriteCollectionInitializer(
        ExpressionSyntax initializerSyntax,
        TypeSyntax elementTypeSyntax,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenInitializer)
    {
        rewrittenInitializer = null!;

        if (semanticModel.GetOperation(initializerSyntax, cancellationToken)?.UnwrapConversions() is not IObjectCreationOperation creation ||
            creation.Initializer is null ||
            !UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                initializerSyntax,
                semanticModel,
                support,
                cancellationToken,
                out var initializerKind) ||
            initializerKind != FrozenSetInitializerKind.CollectionInitializer)
        {
            return false;
        }

        var syntaxInitializer = creation.Syntax switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.Initializer,
            _ => null
        };

        if (syntaxInitializer is null)
            return false;

        var arrayInitializer = SyntaxFactory.InitializerExpression(
            SyntaxKind.ArrayInitializerExpression,
            syntaxInitializer.Expressions);

        var arrayType = SyntaxFactory.ArrayType(
            elementTypeSyntax.WithoutTrivia(),
            SyntaxFactory.SingletonList(
                SyntaxFactory.ArrayRankSpecifier(
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression()))));

        var arrayCreation = SyntaxFactory.ArrayCreationExpression(arrayType, arrayInitializer);
        var comparerArgument = creation.Arguments.Length == 1
            ? creation.Arguments[0].Value.Syntax as ExpressionSyntax
            : null;

        rewrittenInitializer = CreateToFrozenSetInvocation(arrayCreation, comparerArgument).WithTriviaFrom(initializerSyntax);
        return true;
    }
}
