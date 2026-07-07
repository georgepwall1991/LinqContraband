using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionAnalyzer
{
    // A primary-key lookup is exempt whether the key predicate sits on the terminal operator
    // (First(x => x.Id == id)) or on a Where step in the receiver chain
    // (Where(x => x.Id == id).First()) - the two are the same single-row-by-key fetch, so flagging
    // only the terminal form is a false positive on one of the most common EF read patterns.
    private static bool IsPrimaryKeyLookupInChain(IInvocationOperation terminal, IOperation receiver, ITypeSymbol entityType)
    {
        if (TryGetPredicateLambda(terminal, out var terminalLambda) &&
            IsPrimaryKeyLookup(terminalLambda, entityType))
        {
            return true;
        }

        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is not IInvocationOperation invocation)
                break;

            if (invocation.TargetMethod.Name == "Where" &&
                TryGetPredicateLambda(invocation, out var whereLambda) &&
                IsPrimaryKeyLookup(whereLambda, entityType))
            {
                return true;
            }

            if (QuerySteps.Contains(invocation.TargetMethod.Name))
            {
                current = invocation.GetInvocationReceiver();
                continue;
            }

            break;
        }

        return false;
    }

    private static bool IsPrimaryKeyLookup(IAnonymousFunctionOperation lambda, ITypeSymbol entityType)
    {
        var primaryKey = entityType.TryFindPrimaryKey();
        if (primaryKey == null)
            return false;

        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp)
            body = returnOp.ReturnedValue;

        if (body == null)
            return false;

        body = body.UnwrapConversions();
        if (body is not IBinaryOperation binary || binary.OperatorKind != BinaryOperatorKind.Equals)
            return false;

        return IsPrimaryKeyProperty(binary.LeftOperand, lambda, primaryKey) ||
               IsPrimaryKeyProperty(binary.RightOperand, lambda, primaryKey);
    }

    private static bool IsPrimaryKeyProperty(IOperation operation, IAnonymousFunctionOperation lambda, string primaryKey)
    {
        var current = operation.UnwrapConversions();
        if (current is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not IParameterReferenceOperation parameterReference)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
            return false;

        return propertyReference.Property.Name == primaryKey;
    }
}
