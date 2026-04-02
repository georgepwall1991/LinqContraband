using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesFixer
{
    private static bool TryCreateFixPlan(
        FieldDeclarationSyntax fieldDeclaration,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out FixPlan plan)
    {
        plan = default;
        var variable = fieldDeclaration.Declaration.Variables[0];
        if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not IFieldSymbol fieldSymbol ||
            fieldSymbol.Type is not INamedTypeSymbol fieldType ||
            fieldType.TypeArguments.Length != 1)
        {
            return false;
        }

        if (variable.Initializer?.Value is not ExpressionSyntax initializerSyntax ||
            !UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                initializerSyntax,
                semanticModel,
                support,
                cancellationToken,
                out var initializerKind))
        {
            return false;
        }

        var elementType = fieldType.TypeArguments[0];
        var elementTypeSyntax = CreateTypeSyntax(elementType);
        if (!TryRewriteInitializer(initializerSyntax, initializerKind, elementTypeSyntax, semanticModel, support, cancellationToken, out var rewrittenInitializer))
            return false;

        var rewrittenVariable = variable.WithInitializer(variable.Initializer.WithValue(rewrittenInitializer));
        var rewrittenType = CreateTypeSyntax(support.FrozenSetType.Construct(elementType))
            .WithTriviaFrom(fieldDeclaration.Declaration.Type);
        var rewrittenFieldDeclaration = fieldDeclaration.WithDeclaration(
                fieldDeclaration.Declaration
                    .WithType(rewrittenType)
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(rewrittenVariable)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        plan = new FixPlan(rewrittenFieldDeclaration);
        return true;
    }

    private static bool TryRewriteInitializer(
        ExpressionSyntax initializerSyntax,
        FrozenSetInitializerKind initializerKind,
        TypeSyntax elementTypeSyntax,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenInitializer)
    {
        rewrittenInitializer = null!;

        switch (initializerKind)
        {
            case FrozenSetInitializerKind.CollectionInitializer:
                return TryRewriteCollectionInitializer(initializerSyntax, elementTypeSyntax, semanticModel, support, cancellationToken, out rewrittenInitializer);
            case FrozenSetInitializerKind.SourceConstructor:
                return TryRewriteSourceConstructor(initializerSyntax, semanticModel, support, cancellationToken, out rewrittenInitializer);
            case FrozenSetInitializerKind.ToHashSetInvocation:
                return TryRewriteToHashSetInvocation(initializerSyntax, semanticModel, support, cancellationToken, out rewrittenInitializer);
            default:
                return false;
        }
    }

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

    private static bool TryRewriteSourceConstructor(
        ExpressionSyntax initializerSyntax,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenInitializer)
    {
        rewrittenInitializer = null!;

        if (semanticModel.GetOperation(initializerSyntax, cancellationToken)?.UnwrapConversions() is not IObjectCreationOperation creation ||
            !UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                initializerSyntax,
                semanticModel,
                support,
                cancellationToken,
                out var initializerKind) ||
            initializerKind != FrozenSetInitializerKind.SourceConstructor ||
            creation.Arguments.Length is < 1 or > 2 ||
            creation.Arguments[0].Value.Syntax is not ExpressionSyntax sourceExpression)
        {
            return false;
        }

        var comparerArgument = creation.Arguments.Length == 2
            ? creation.Arguments[1].Value.Syntax as ExpressionSyntax
            : null;

        rewrittenInitializer = CreateToFrozenSetInvocation(sourceExpression, comparerArgument).WithTriviaFrom(initializerSyntax);
        return true;
    }

    private static bool TryRewriteToHashSetInvocation(
        ExpressionSyntax initializerSyntax,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenInitializer)
    {
        rewrittenInitializer = null!;

        if (semanticModel.GetOperation(initializerSyntax, cancellationToken)?.UnwrapConversions() is not IInvocationOperation ||
            !UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                initializerSyntax,
                semanticModel,
                support,
                cancellationToken,
                out var initializerKind) ||
            initializerKind != FrozenSetInitializerKind.ToHashSetInvocation ||
            initializerSyntax is not InvocationExpressionSyntax invocationSyntax ||
            invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        rewrittenInitializer = invocationSyntax
            .WithExpression(memberAccess.WithName(SyntaxFactory.IdentifierName("ToFrozenSet")))
            .WithTriviaFrom(initializerSyntax);
        return true;
    }

    private static ExpressionSyntax CreateToFrozenSetInvocation(ExpressionSyntax sourceExpression, ExpressionSyntax? comparerArgument)
    {
        var receiver = ParenthesizeIfNeeded(sourceExpression.WithoutTrivia());
        var memberAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("ToFrozenSet"));

        if (comparerArgument is null)
            return SyntaxFactory.InvocationExpression(memberAccess);

        return SyntaxFactory.InvocationExpression(
            memberAccess,
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(comparerArgument.WithoutTrivia()))));
    }

    private static TypeSyntax CreateTypeSyntax(ITypeSymbol typeSymbol)
    {
        return SyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithAdditionalAnnotations(Simplifier.Annotation);
    }

    private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax => expression,
            GenericNameSyntax => expression,
            MemberAccessExpressionSyntax => expression,
            InvocationExpressionSyntax => expression,
            ElementAccessExpressionSyntax => expression,
            ThisExpressionSyntax => expression,
            BaseExpressionSyntax => expression,
            ParenthesizedExpressionSyntax => expression,
            ArrayCreationExpressionSyntax => expression,
            ImplicitArrayCreationExpressionSyntax => expression,
            ImplicitObjectCreationExpressionSyntax => expression,
            ObjectCreationExpressionSyntax => expression,
            _ => SyntaxFactory.ParenthesizedExpression(expression)
        };
    }
}
