using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    private static bool RewriteStaticQueryableInvocation(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        MemberAccessExpressionSyntax memberAccess,
        ExpressionSyntax enumerableQualifier,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputSequenceArgument(semanticModel, queryInvocation, cancellationToken, out var sourceArgument))
            return false;

        var source = sourceArgument.Expression;
        var rewritesOrderedSource = IsThenBy(memberAccess.Name.Identifier.ValueText);
        var sourceWasRewritten = rewritesOrderedSource &&
            (RewriteStaticQueryableSourceChain(editor, semanticModel, source, cancellationToken) ||
             RewriteQueryableExtensionOrderingSourceChain(editor, semanticModel, source, cancellationToken));

        if (!sourceWasRewritten && rewritesOrderedSource)
            return false;

        if (!sourceWasRewritten && !IsInvocationOf(source, "AsEnumerable"))
        {
            var asEnumerableInvocation = CreateAsEnumerableInvocation(source);
            editor.ReplaceNode(source, asEnumerableInvocation.WithTriviaFrom(source));
        }

        editor.ReplaceNode(memberAccess.Expression, enumerableQualifier.WithTriviaFrom(memberAccess.Expression));

        return true;
    }

    private static bool RewriteStaticQueryableSourceChain(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (source is not InvocationExpressionSyntax sourceInvocation ||
            sourceInvocation.Expression is not MemberAccessExpressionSyntax sourceMemberAccess ||
            !IsSystemLinqQueryableType(semanticModel, sourceMemberAccess.Expression, cancellationToken) ||
            !CanSwitchStaticQueryableMethodToEnumerable(semanticModel, sourceMemberAccess))
            return false;

        var enumerableQualifier = CreateEnumerableQualifier(sourceMemberAccess.Expression);
        return RewriteStaticQueryableInvocation(
            editor,
            semanticModel,
            sourceInvocation,
            sourceMemberAccess,
            enumerableQualifier,
            cancellationToken);
    }

    private static void RewriteEnclosingStaticQueryableContinuations(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax currentExpression = queryInvocation;

        while (currentExpression.Parent is ArgumentSyntax argument &&
               argument.Parent is ArgumentListSyntax argumentList &&
               argumentList.Parent is InvocationExpressionSyntax parentInvocation &&
               TryGetInputSequenceArgument(semanticModel, parentInvocation, cancellationToken, out var sourceArgument) &&
               argument.Span == sourceArgument.Span &&
               parentInvocation.Expression is MemberAccessExpressionSyntax parentMemberAccess &&
               IsSystemLinqQueryableType(semanticModel, parentMemberAccess.Expression, cancellationToken) &&
               CanSwitchStaticQueryableMethodToEnumerable(semanticModel, parentMemberAccess))
        {
            var enumerableQualifier = CreateEnumerableQualifier(parentMemberAccess.Expression);
            editor.ReplaceNode(parentMemberAccess.Expression, enumerableQualifier.WithTriviaFrom(parentMemberAccess.Expression));
            currentExpression = parentInvocation;
        }
    }

}
