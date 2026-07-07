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
                if (IsKnownLinqWhere(invocation.TargetMethod))
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

    private static bool HasQuerySyntaxWhere(SyntaxNode syntax)
    {
        return syntax
            .DescendantNodesAndSelf()
            .OfType<QueryExpressionSyntax>()
            .Any(query => query.Body.Clauses.OfType<WhereClauseSyntax>().Any());
    }
}
