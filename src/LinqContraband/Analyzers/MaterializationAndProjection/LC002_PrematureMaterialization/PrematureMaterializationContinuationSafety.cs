using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool HasProviderSafeContinuationArguments(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type is not INamedTypeSymbol parameterType ||
                !IsDelegateLikeParameter(parameterType))
            {
                continue;
            }

            var value = argument.Value.UnwrapConversions();
            if (value is IDelegateCreationOperation delegateCreation)
                value = delegateCreation.Target.UnwrapConversions();

            if (value is not IAnonymousFunctionOperation anonymousFunction ||
                !IsProviderSafeLambdaBody(anonymousFunction.Body))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDelegateLikeParameter(INamedTypeSymbol parameterType)
    {
        return parameterType.TypeKind == TypeKind.Delegate ||
               parameterType.DelegateInvokeMethod != null ||
               (parameterType.Name == "Expression" &&
                parameterType.ContainingNamespace?.ToString() == "System.Linq.Expressions");
    }

    private static bool IsProviderSafeLambdaBody(IOperation body)
    {
        var unwrapped = body.UnwrapConversions();

        if (unwrapped is IBlockOperation block)
        {
            return block.Operations.Length == 1 &&
                   block.Operations[0] is IReturnOperation blockReturn &&
                   blockReturn.ReturnedValue != null &&
                   IsProviderSafeExpression(blockReturn.ReturnedValue);
        }

        if (unwrapped is IReturnOperation returnOperation)
        {
            return returnOperation.ReturnedValue != null &&
                   IsProviderSafeExpression(returnOperation.ReturnedValue);
        }

        return IsProviderSafeExpression(unwrapped);
    }

    private static bool IsProviderSafeExpression(IOperation operation)
    {
        var unwrapped = operation.UnwrapConversions();

        switch (unwrapped)
        {
            case ILiteralOperation:
            case IParameterReferenceOperation:
            case ILocalReferenceOperation:
            case IFieldReferenceOperation:
            case IPropertyReferenceOperation:
            case IConditionalAccessInstanceOperation:
            case IInstanceReferenceOperation:
                return true;

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

    private static bool IsAllowedProviderSafeStringMethod(IMethodSymbol method)
    {
        if (method.ContainingType.SpecialType != SpecialType.System_String)
            return false;

        if (HasStringComparisonParameter(method))
            return false;

        return method.Name is
            "Contains" or
            "StartsWith" or
            "EndsWith" or
            "IsNullOrEmpty" or
            "IsNullOrWhiteSpace";
    }

    private static bool HasStringComparisonParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.Name == "StringComparison" &&
                parameter.Type.ContainingNamespace?.ToString() == "System")
            {
                return true;
            }
        }

        return false;
    }
}
