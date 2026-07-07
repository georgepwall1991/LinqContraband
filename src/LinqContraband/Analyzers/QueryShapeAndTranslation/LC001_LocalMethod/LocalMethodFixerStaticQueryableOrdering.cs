using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    private static bool RewriteQueryableExtensionOrderingSourceChain(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (source is not InvocationExpressionSyntax sourceInvocation ||
            sourceInvocation.Expression is not MemberAccessExpressionSyntax sourceMemberAccess ||
            !IsQueryableOrderingInvocation(semanticModel, sourceInvocation, cancellationToken))
            return false;

        var receiver = sourceMemberAccess.Expression;
        if (!RewriteStaticQueryableSourceChain(editor, semanticModel, receiver, cancellationToken) &&
            !RewriteQueryableExtensionOrderingSourceChain(editor, semanticModel, receiver, cancellationToken) &&
            !IsInvocationOf(receiver, "AsEnumerable"))
        {
            var asEnumerableInvocation = CreateAsEnumerableInvocation(receiver);
            editor.ReplaceNode(receiver, asEnumerableInvocation.WithTriviaFrom(receiver));
        }

        return true;
    }

    private static bool IsRewritableOrderedSource(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        return IsStaticQueryableInvocation(semanticModel, source, cancellationToken) ||
               IsQueryableOrderingInvocation(semanticModel, source, cancellationToken);
    }

    private static bool IsStaticQueryableInvocation(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        return source is InvocationExpressionSyntax sourceInvocation &&
               sourceInvocation.Expression is MemberAccessExpressionSyntax sourceMemberAccess &&
               IsSystemLinqQueryableType(semanticModel, sourceMemberAccess.Expression, cancellationToken);
    }

    private static bool IsQueryableOrderingInvocation(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null || source is not InvocationExpressionSyntax sourceInvocation)
            return false;

        var symbol = semanticModel.GetSymbolInfo(sourceInvocation, cancellationToken).Symbol as IMethodSymbol;
        var method = symbol?.ReducedFrom ?? symbol;

        return method?.ContainingType?.Name == "Queryable" &&
               method.ContainingType.ContainingNamespace.ToDisplayString() == "System.Linq" &&
               IsOrderingMethod(method.Name);
    }
}
