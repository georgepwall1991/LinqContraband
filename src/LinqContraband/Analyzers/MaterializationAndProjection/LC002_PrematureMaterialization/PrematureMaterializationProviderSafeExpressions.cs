using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool IsProviderSafeExpression(IOperation operation)
    {
        var unwrapped = operation.UnwrapConversions();

        switch (unwrapped)
        {
            case ILiteralOperation:
            case IParameterReferenceOperation:
            case ILocalReferenceOperation:
            case IFieldReferenceOperation:
            case IConditionalAccessInstanceOperation:
            case IInstanceReferenceOperation:
                return true;

            case IPropertyReferenceOperation property:
                return IsProviderSafeProperty(property);

            case IUnaryOperation unary:
                return IsProviderSafeExpression(unary.Operand);

            case IBinaryOperation binary:
                return IsProviderSafeExpression(binary.LeftOperand) &&
                       IsProviderSafeExpression(binary.RightOperand);

            case IConditionalOperation conditional:
                return conditional.WhenTrue != null &&
                       conditional.WhenFalse != null &&
                       IsProviderSafeExpression(conditional.Condition) &&
                       IsProviderSafeExpression(conditional.WhenTrue) &&
                       IsProviderSafeExpression(conditional.WhenFalse);

            case IIsTypeOperation isType:
                return isType.ValueOperand != null &&
                       IsProviderSafeExpression(isType.ValueOperand);

            case IParenthesizedOperation parenthesized:
                return IsProviderSafeExpression(parenthesized.Operand);

            case IConversionOperation conversion:
                return IsProviderSafeExpression(conversion.Operand);

            case IAnonymousObjectCreationOperation anonymousObject:
                foreach (var initializer in anonymousObject.Initializers)
                {
                    if (!IsProviderSafeExpression(initializer))
                        return false;
                }

                return true;

            case ISimpleAssignmentOperation assignment:
                return IsProviderSafeExpression(assignment.Value);

            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    if (!IsProviderSafeExpression(element))
                        return false;
                }

                return true;

            case IObjectCreationOperation objectCreation:
                return IsProviderSafeObjectCreation(objectCreation);

            case IInvocationOperation invocation:
                return IsProviderSafeInvocation(invocation);

            case IConditionalAccessOperation conditionalAccess:
                return IsProviderSafeExpression(conditionalAccess.Operation);

            case ICoalesceOperation coalesce:
                return IsProviderSafeExpression(coalesce.Value) &&
                       IsProviderSafeExpression(coalesce.WhenNull);

            default:
                return false;
        }
    }

    // A property read from the lambda's row must map to a column (or be a translatable BCL member such as
    // string.Length). A computed or [NotMapped] property only works on the client, so moving the operator
    // into SQL would throw "could not be translated".
    private static bool IsProviderSafeProperty(IPropertyReferenceOperation reference)
    {
        if (reference.Instance == null || !ReadsLambdaParameter(reference.Instance))
            return true;

        if (!IsProviderSafeExpression(reference.Instance))
            return false;

        var property = reference.Property;
        var containingType = property.ContainingType;
        if (containingType == null)
            return false;

        if (containingType.IsAnonymousType || containingType.IsTupleType)
            return true;

        if (property.IsIndexer)
            return containingType.SpecialType == SpecialType.System_String;

        if (IsSystemNamespace(containingType.ContainingNamespace))
            return true;

        return property.SetMethod != null && !HasNotMappedAttribute(property);
    }

    private static bool ReadsLambdaParameter(IOperation operation)
    {
        foreach (var node in operation.DescendantsAndSelf())
        {
            if (node is IParameterReferenceOperation { Parameter.ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } })
                return true;
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol? ns)
    {
        for (var current = ns; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            if (current.ContainingNamespace is { IsGlobalNamespace: true })
                return current.Name == "System";
        }

        return false;
    }

    private static bool HasNotMappedAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "NotMappedAttribute")
                return true;
        }

        return false;
    }

    private static bool IsProviderSafeObjectCreation(IObjectCreationOperation objectCreation)
    {
        if (objectCreation.Constructor?.ContainingType?.IsAnonymousType == true)
            return true;

        if (objectCreation.Constructor?.ContainingType?.IsTupleType == true)
        {
            foreach (var argument in objectCreation.Arguments)
            {
                if (!IsProviderSafeExpression(argument.Value))
                    return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsProviderSafeInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!IsAllowedProviderSafeStringMethod(method))
            return false;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver != null && !IsProviderSafeExpression(receiver))
            return false;

        foreach (var argument in invocation.Arguments)
        {
            if (!IsProviderSafeExpression(argument.Value))
                return false;
        }

        return true;
    }

}
