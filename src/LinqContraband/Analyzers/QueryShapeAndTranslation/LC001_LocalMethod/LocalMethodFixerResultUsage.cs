using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    // After the fix the query is an IEnumerable<T>. Withhold it when the rest of the chain, or where the
    // result lands, still needs an IQueryable<T>: EF Core async terminals, Include, an IQueryable local,
    // parameter or return type. Those rewrites did not compile.
    private static bool ResultStillNeedsQueryable(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return true;

        return ExpressionNeedsQueryable(semanticModel, queryInvocation, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), cancellationToken);
    }

    private static bool ExpressionNeedsQueryable(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        var current = expression;

        while (true)
        {
            var parent = current.Parent;
            while (parent is ParenthesizedExpressionSyntax)
            {
                current = (ExpressionSyntax)parent;
                parent = current.Parent;
            }

            InvocationExpressionSyntax? next = null;
            if (parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression == current &&
                memberAccess.Parent is InvocationExpressionSyntax receiverInvocation)
            {
                next = receiverInvocation;
            }
            else if (parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax argumentInvocation } } argument &&
                     IsSystemLinqQueryableSource(semanticModel, argumentInvocation, argument, cancellationToken))
            {
                next = argumentInvocation;
            }

            if (next == null) break;

            if (semanticModel.GetSymbolInfo(next, cancellationToken).Symbol is not IMethodSymbol method)
                return true;

            if (IsSystemLinqQueryableMethod(method))
            {
                if (PassesStoredExpression(semanticModel, next, cancellationToken))
                    return true;

                current = next;
                continue;
            }

            // Any other method must accept the IEnumerable<T> the fix produces.
            var receiverType = method.ReducedFrom?.Parameters.FirstOrDefault()?.Type ??
                               (method.IsExtensionMethod ? method.Parameters.FirstOrDefault()?.Type : method.ContainingType);
            return receiverType.IsIQueryable();
        }

        var convertedType = semanticModel.GetTypeInfo(current, cancellationToken).ConvertedType;
        if (convertedType.IsIQueryable() && LandsInTypedTarget(current))
        {
            // `var query = ...` takes the new type, so look at how the local is used instead.
            if (current.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } &&
                declarator.Parent is VariableDeclarationSyntax { Type.IsVar: true } &&
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol local)
            {
                return LocalNeedsQueryable(semanticModel, declarator, local, visitedLocals, cancellationToken);
            }

            return true;
        }

        return false;
    }

    // Places where the expression's type must convert to a declared type. A foreach, a statement or a
    // string interpolation reads the result as a sequence, which IEnumerable<T> still is.
    private static bool LandsInTypedTarget(ExpressionSyntax expression)
    {
        return expression.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Right == expression,
            ForEachStatementSyntax => false,
            ExpressionStatementSyntax => false,
            InterpolationSyntax => false,
            AwaitExpressionSyntax => false,
            _ => true
        };
    }

    private static bool LocalNeedsQueryable(
        SemanticModel semanticModel,
        VariableDeclaratorSyntax declarator,
        ILocalSymbol local,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        if (!visitedLocals.Add(local)) return false;

        var scope = declarator.FirstAncestorOrSelf<BlockSyntax>() ?? (SyntaxNode?)declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope == null) return true;

        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != local.Name ||
                !SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
                continue;

            // A later `query = <IQueryable>` still compiles against an IEnumerable<T> local.
            if (identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
                continue;

            if (identifier.Parent is ArgumentSyntax refArgument && !refArgument.RefKindKeyword.IsKind(SyntaxKind.None))
                return true;

            if (ExpressionNeedsQueryable(semanticModel, identifier, visitedLocals, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsSystemLinqQueryableMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.ContainingType?.Name == "Queryable" &&
               definition.ContainingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
    }

    private static bool IsSystemLinqQueryableSource(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !IsSystemLinqQueryableMethod(operation.TargetMethod) ||
            operation.Instance != null)
            return false;

        return GetInputSequenceArgument(operation)?.Syntax == argument;
    }

    private static bool PassesStoredExpression(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is AnonymousFunctionExpressionSyntax) continue;

            var type = semanticModel.GetTypeInfo(argument.Expression, cancellationToken).ConvertedType;
            if (type is INamedTypeSymbol { Name: "Expression", IsGenericType: true } &&
                type.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
                return true;
        }

        return false;
    }

    // The local method is called on a sub-query of the row, such as `g.Items.Where(...).Restrict(r).Count()`
    // inside a projection. AsEnumerable() on the outer query would run the whole projection on the client,
    // where navigations such as `g.Items` are not loaded.
    private static bool LocalCallReadsRowSubQuery(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax localInvocation,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return true;

        var queryLambda = localInvocation.Ancestors()
            .TakeWhile(node => node != queryInvocation)
            .OfType<LambdaExpressionSyntax>()
            .LastOrDefault();

        if (queryLambda == null ||
            semanticModel.GetOperation(queryLambda, cancellationToken) is not IAnonymousFunctionOperation lambdaOperation)
            return false;

        foreach (var node in localInvocation.AncestorsAndSelf().TakeWhile(node => node != queryLambda))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
                continue;

            var sequence = operation.Instance ?? (operation.TargetMethod.IsExtensionMethod ? operation.Arguments.FirstOrDefault()?.Value : null);
            if (sequence == null || !IsNonStringSequence(sequence.Type)) continue;

            foreach (var parameter in lambdaOperation.Symbol.Parameters)
            {
                if (sequence.ReferencesParameter(parameter))
                    return true;
            }
        }

        return false;
    }

    private static bool IsNonStringSequence(ITypeSymbol? type)
    {
        if (type == null || type.SpecialType == SpecialType.System_String) return false;

        return type.AllInterfaces.Append(type as INamedTypeSymbol).Any(candidate =>
            candidate is { Name: "IEnumerable", IsGenericType: true } &&
            candidate.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic");
    }
}
