using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection;

public sealed partial class IncludeIgnoredByProjectionAnalyzer
{
    /// <summary>
    /// Returns true when the projection could hand an entity instance back to EF Core, in which case EF may
    /// apply the Include to it (for example <c>o =&gt; o</c>, <c>o =&gt; new { o }</c>, <c>o =&gt; o.Customer</c>, or
    /// <c>o =&gt; Map(o)</c>). An entity value is only allowed as the instance of a member access
    /// (<c>o.Customer.Name</c>), in a null comparison, or as the source of a LINQ operator whose own result is
    /// checked in turn (<c>o.Lines.Count()</c>, <c>o.Lines.Select(l =&gt; new LineDto { Sku = l.Sku })</c>).
    /// </summary>
    private static bool ProjectionMayReturnEntities(IAnonymousFunctionOperation lambda)
    {
        foreach (var operation in lambda.Body.Descendants())
        {
            if (operation is IConversionOperation or IParenthesizedOperation) continue;
            if (!IsEntityValued(operation)) continue;
            if (IsConsumedAsMemberInstance(operation) ||
                IsConsumedAsLinqSource(operation) ||
                IsComparedWithNull(operation))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the value is (or may be) an entity read by the query: a lambda parameter, a navigation reached
    /// from one, or a LINQ operator that passes such values through. Values the projection constructs itself
    /// (DTOs, anonymous objects) are not entity values even when their type is a user class.
    /// </summary>
    private static bool IsEntityValued(IOperation operation)
    {
        var value = UnwrapValue(operation);
        switch (value)
        {
            case IParameterReferenceOperation parameter:
                return IsEntityLike(parameter.Type);
            case IPropertyReferenceOperation property:
                return IsEntityLike(property.Type) && property.Instance != null && IsEntityValued(property.Instance);
            case IFieldReferenceOperation field:
                return IsEntityLike(field.Type) && field.Instance != null && IsEntityValued(field.Instance);
            case IConditionalOperation conditional:
                return conditional.WhenTrue != null && IsEntityValued(conditional.WhenTrue) ||
                       conditional.WhenFalse != null && IsEntityValued(conditional.WhenFalse);
            case ICoalesceOperation coalesce:
                return IsEntityValued(coalesce.Value) || IsEntityValued(coalesce.WhenNull);
            case IInvocationOperation invocation:
                return IsEntityValuedInvocation(invocation);
            default:
                return false;
        }
    }

    private static bool IsEntityValuedInvocation(IInvocationOperation invocation)
    {
        if (!IsEntityLike(invocation.Type)) return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.ContainingType?.ToDisplayString() is "System.Linq.Enumerable" or "System.Linq.Queryable")
        {
            if (method.Name is "Select" or "SelectMany")
            {
                // The result holds entities only when the selector returns one.
                foreach (var argument in invocation.Arguments)
                {
                    if (TryGetSelector(argument.Value) is { } selector)
                        return ReturnsEntityValue(selector);
                }

                return true;
            }

            return invocation.Arguments.Length > 0 && IsEntityValued(invocation.Arguments[0].Value);
        }

        // Unknown helpers: treat as entity-valued when any input is, e.g. Map(o) or o.Customer.Self().
        if (invocation.Instance != null && IsEntityValued(invocation.Instance)) return true;
        foreach (var argument in invocation.Arguments)
        {
            if (IsEntityValued(argument.Value)) return true;
        }

        return false;
    }

    private static bool ReturnsEntityValue(IAnonymousFunctionOperation selector)
    {
        foreach (var operation in selector.Body.Descendants())
        {
            if (operation is IReturnOperation { ReturnedValue: { } returned } && IsEntityValued(returned))
                return true;
        }

        return false;
    }

    private static IOperation UnwrapValue(IOperation operation)
    {
        var current = operation;
        while (current is IConversionOperation or IParenthesizedOperation)
        {
            current = current switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => current
            };
        }

        return current;
    }

    private static IOperation? GetConsumer(IOperation operation, out IOperation consumedValue)
    {
        consumedValue = operation;
        var parent = operation.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation)
        {
            consumedValue = parent;
            parent = parent.Parent;
        }

        return parent;
    }

    private static bool IsConsumedAsMemberInstance(IOperation operation)
    {
        var consumer = GetConsumer(operation, out var consumedValue);
        return consumer is IPropertyReferenceOperation { Property.IsIndexer: false } property &&
               ReferenceEquals(property.Instance, consumedValue) ||
               consumer is IFieldReferenceOperation field && ReferenceEquals(field.Instance, consumedValue);
    }

    private static bool IsComparedWithNull(IOperation operation)
    {
        if (GetConsumer(operation, out var consumedValue) is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            } comparison)
        {
            return false;
        }

        var other = ReferenceEquals(comparison.LeftOperand, consumedValue) ? comparison.RightOperand : comparison.LeftOperand;
        return other.UnwrapConversions() is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    private static bool IsConsumedAsLinqSource(IOperation operation)
    {
        if (GetConsumer(operation, out _) is not IArgumentOperation argument ||
            argument.Parent is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var containingType = method.ContainingType?.ToDisplayString();
        return containingType is "System.Linq.Enumerable" or "System.Linq.Queryable" &&
               argument.Parameter?.Ordinal == 0;
    }

    private static bool IsEntityLike(ITypeSymbol? type)
    {
        if (type == null) return false;
        if (type.TypeKind == TypeKind.TypeParameter) return true;
        if (type.SpecialType != SpecialType.None) return false;
        if (type.IsValueType || type.IsAnonymousType || type.TypeKind == TypeKind.Delegate) return false;

        if (IncludePathParser.TryGetCollectionElementType(type, out var elementType))
            return IsEntityLike(elementType);

        var ns = type.ContainingNamespace?.ToString() ?? string.Empty;
        return !(ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
                 ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal));
    }
}
