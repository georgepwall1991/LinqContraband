using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    private static InvocationExpressionSyntax? FindQueryInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel?.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return null;

        var parent = invocation.Parent;
        var lambdas = new List<IAnonymousFunctionOperation>();

        while (parent != null)
        {
            if (parent is LambdaExpressionSyntax lambda &&
                semanticModel.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation lambdaOperation)
            {
                lambdas.Add(lambdaOperation);
            }

            if (parent is InvocationExpressionSyntax queryInvocation &&
                IsQueryableInvocation(semanticModel, queryInvocation, cancellationToken) &&
                lambdas.Count > 0 &&
                InvocationDependsOnLambdaParameter(invocationOperation, lambdas[lambdas.Count - 1]))
            {
                return queryInvocation;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static bool IsNestedQueryInvocation(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return false;

        foreach (var lambda in queryInvocation.Ancestors().OfType<LambdaExpressionSyntax>())
        {
            var enclosingInvocation = lambda.Parent?.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (enclosingInvocation != null &&
                IsQueryableInvocation(semanticModel, enclosingInvocation, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueryableInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;

        var type = operation.Instance?.Type;
        if (type == null)
            type = GetInputSequenceArgument(operation)?.Value.Type;

        return type.IsIQueryable();
    }

    private static IArgumentOperation? GetInputSequenceArgument(IInvocationOperation invocation)
    {
        IArgumentOperation? firstArgument = null;
        IArgumentOperation? namedSequenceArgument = null;

        foreach (var argument in invocation.Arguments)
        {
            firstArgument ??= argument;

            if (argument.Parameter?.Type.IsIQueryable() == true)
                return argument;

            if (argument.Parameter?.Name is "source" or "outer")
                namedSequenceArgument ??= argument;
        }

        return namedSequenceArgument ?? firstArgument;
    }

    private static bool InvocationDependsOnLambdaParameter(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda)
    {
        foreach (var parameter in lambda.Symbol.Parameters)
        {
            if (invocation.ReferencesParameter(parameter))
                return true;
        }

        return false;
    }

    private static bool CanRewriteQueryInvocation(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (queryInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (!IsSystemLinqQueryableType(semanticModel, memberAccess.Expression, cancellationToken))
            return true;

        if (!CanSwitchStaticQueryableMethodToEnumerable(semanticModel, memberAccess))
            return false;

        if (!TryGetInputSequenceArgument(semanticModel, queryInvocation, cancellationToken, out var sourceArgument))
            return false;

        return !IsThenBy(memberAccess.Name.Identifier.ValueText) ||
               IsRewritableOrderedSource(semanticModel, sourceArgument.Expression, cancellationToken);
    }
}
