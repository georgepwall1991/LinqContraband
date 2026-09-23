using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer
{
    private static bool HasWhereInChain(IOperation? operation, CancellationToken cancellationToken)
    {
        return HasWhereInChain(
            operation,
            cancellationToken,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool HasWhereInChain(
        IOperation? operation,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        var current = operation;

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (IsKnownLinqWhere(invocation.TargetMethod) || IsPredicateWhere(invocation))
                    return true;

                current = invocation.GetInvocationReceiver();
                continue;
            }

            if (current is ITranslatedQueryOperation translatedQuery)
            {
                if (HasQuerySyntaxWhere(translatedQuery.Syntax))
                    return true;

                current = translatedQuery.Operation;
                continue;
            }

            if (current is IConditionalOperation conditional)
            {
                return HasWhereInChain(conditional.WhenTrue, cancellationToken, ForkVisitedLocals(visitedLocals)) &&
                       HasWhereInChain(conditional.WhenFalse, cancellationToken, ForkVisitedLocals(visitedLocals));
            }

            if (current is ISwitchExpressionOperation switchExpression)
            {
                return switchExpression.Arms.Length > 0 &&
                       switchExpression.Arms.All(arm =>
                           HasWhereInChain(arm.Value, cancellationToken, ForkVisitedLocals(visitedLocals)));
            }

            if (current is ILocalReferenceOperation localReference)
                return HasWhereInLocalInitializer(localReference, cancellationToken, visitedLocals);

            if (current is IParameterReferenceOperation or IFieldReferenceOperation or IPropertyReferenceOperation)
                return false;

            if (current.Type.IsDbSet() || current.Type.IsIQueryable())
                return false;

            current = current.Parent;
        }

        return false;
    }

    private static bool IsKnownLinqWhere(IMethodSymbol method)
    {
        return method.Name == "Where" &&
               method.ContainingNamespace?.ToString() == "System.Linq" &&
               method.ContainingType?.Name is "Queryable" or "Enumerable";
    }

    // A project's own `Where` overload that takes a predicate, such as BTCPay Server's
    // `Where<T>(this DbSet<T>, Expression<Func<T, bool>>)`, filters when the call passes a lambda.
    // A `Where` taking anything else, such as a string reason, is not a proven filter.
    private static bool IsPredicateWhere(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Where")
            return false;

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value is IDelegateCreationOperation delegateCreation)
                value = delegateCreation.Target;

            if (value is IAnonymousFunctionOperation &&
                IsPredicateExpressionType(argument.Parameter?.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPredicateExpressionType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Expression" } expression &&
               expression.ContainingNamespace?.ToString() == "System.Linq.Expressions" &&
               expression.TypeArguments.Length == 1 &&
               expression.TypeArguments[0] is INamedTypeSymbol { Name: "Func" } func &&
               func.TypeArguments.Length == 2 &&
               func.TypeArguments[1].SpecialType == SpecialType.System_Boolean;
    }

    private static bool HasQuerySyntaxWhere(SyntaxNode syntax)
    {
        return syntax
            .DescendantNodesAndSelf()
            .OfType<QueryExpressionSyntax>()
            .Any(query => query.Body.Clauses.OfType<WhereClauseSyntax>().Any());
    }
}
