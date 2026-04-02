using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionAnalyzer
{
    private static bool TryGetEntityType(IOperation receiver, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                var name = invocation.TargetMethod.Name;
                if (name == "Select")
                    return false;

                if (QuerySteps.Contains(name))
                {
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                if (name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                {
                    var sequenceElementType = GetSequenceElementType(invocation.Type);
                    if (sequenceElementType != null)
                    {
                        entityType = sequenceElementType;
                        return true;
                    }
                }

                return false;
            }

            var currentType = current.Type;
            if (currentType == null)
                return false;

            if (currentType.IsDbSet() || currentType.IsIQueryable())
            {
                var sequenceElementType = GetSequenceElementType(currentType);
                if (sequenceElementType == null)
                    return false;

                entityType = sequenceElementType;
                return true;
            }

            if (current is IPropertyReferenceOperation or IFieldReferenceOperation or ILocalReferenceOperation or IParameterReferenceOperation)
                return false;

            return false;
        }

        return false;
    }

    private static bool HasSelectInChain(IOperation receiver)
    {
        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (invocation.TargetMethod.Name == "Select")
                    return true;

                if (QuerySteps.Contains(invocation.TargetMethod.Name))
                {
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                return false;
            }

            break;
        }

        return false;
    }

    private static bool TryGetPredicateLambda(IInvocationOperation invocation, out IAnonymousFunctionOperation lambda)
    {
        lambda = null!;

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value is IAnonymousFunctionOperation anonymousFunction)
            {
                lambda = anonymousFunction;
                return true;
            }
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

    private static INamedTypeSymbol? GetSequenceElementType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
            return null;

        if (namedType.TypeArguments.Length > 0 && namedType.TypeArguments[0] is INamedTypeSymbol namedArgument)
            return namedArgument;

        return null;
    }
}
