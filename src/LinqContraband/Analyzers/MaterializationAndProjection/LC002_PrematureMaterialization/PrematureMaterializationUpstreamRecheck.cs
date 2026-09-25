using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

// `(await query.Where(x => x.Key == key).ToArrayAsync()).SingleOrDefault(x => x.Key == key)` re-applies a filter
// the database already ran. The in-memory copy is deliberate (a case-insensitive collation can match rows the
// exact comparison rejects), so folding it back into SQL would remove the check rather than move it.
public sealed partial class PrematureMaterializationAnalyzer
{
    private static readonly ImmutableHashSet<string> PredicateContinuationMethods = ImmutableHashSet.Create(
        "Where",
        "Any",
        "All",
        "Count",
        "LongCount",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault"
    );

    private static bool IsInMemoryRecheckOfUpstreamFilter(IInvocationOperation continuation, IOperation materializedReceiver)
    {
        if (!PredicateContinuationMethods.Contains(continuation.TargetMethod.Name)) return false;
        if (!TryGetSinglePredicate(continuation, out var predicate, out var predicateParameter)) return false;

        if (materializedReceiver.UnwrapConversions() is not IInvocationOperation materializer) return false;

        var upstream = new List<(IOperation Conjunct, IParameterSymbol Parameter)>();
        CollectUpstreamWhereConjuncts(
            materializer.GetInvocationReceiver(),
            continuation.FindOwningExecutableRoot(),
            continuation.Syntax.SpanStart,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            upstream);
        if (upstream.Count == 0) return false;

        var conjuncts = new List<IOperation>();
        SplitConjuncts(predicate, conjuncts);

        foreach (var conjunct in conjuncts)
        {
            var matched = false;
            foreach (var candidate in upstream)
            {
                if (AreEquivalent(conjunct, predicateParameter, candidate.Conjunct, candidate.Parameter))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched) return false;
        }

        return true;
    }

    private static void CollectUpstreamWhereConjuncts(
        IOperation? operation,
        IOperation? executableRoot,
        int position,
        HashSet<ILocalSymbol> visitedLocals,
        List<(IOperation Conjunct, IParameterSymbol Parameter)> conjuncts)
    {
        var current = operation?.UnwrapConversions();
        while (current != null)
        {
            if (current is ILocalReferenceOperation localReference)
            {
                if (executableRoot == null ||
                    !TryResolveSingleAssignedValue(executableRoot, localReference.Local, position, visitedLocals, out var assigned))
                {
                    return;
                }

                current = assigned.UnwrapConversions();
                continue;
            }

            if (current is not IInvocationOperation invocation) return;

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null) return;

            // Walk only through operators that keep the element type, so a filter on the far side of a
            // projection is never mistaken for one on the rows that were materialized.
            var receiverElement = GetQueryableElementType(receiver.Type);
            var resultElement = GetQueryableElementType(invocation.Type);
            if (receiverElement == null || resultElement == null ||
                !SymbolEqualityComparer.Default.Equals(receiverElement, resultElement))
            {
                return;
            }

            if (IsQueryableWhere(invocation.TargetMethod) &&
                TryGetSinglePredicate(invocation, out var predicate, out var parameter))
            {
                var split = new List<IOperation>();
                SplitConjuncts(predicate, split);
                foreach (var conjunct in split)
                    conjuncts.Add((conjunct, parameter));
            }

            current = receiver;
        }
    }

    private static bool IsQueryableWhere(IMethodSymbol method)
    {
        return method.Name == "Where" &&
               method.ContainingType.Name == "Queryable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static ITypeSymbol? GetQueryableElementType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named) return null;

        if (IsGenericIQueryable(named)) return named.TypeArguments[0];

        foreach (var candidate in named.AllInterfaces)
        {
            if (IsGenericIQueryable(candidate)) return candidate.TypeArguments[0];
        }

        return null;
    }

    private static bool IsGenericIQueryable(INamedTypeSymbol type)
    {
        return type.Name == "IQueryable" &&
               type.TypeArguments.Length == 1 &&
               type.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static bool TryGetSinglePredicate(
        IInvocationOperation invocation,
        out IOperation predicate,
        out IParameterSymbol parameter)
    {
        predicate = null!;
        parameter = null!;

        IAnonymousFunctionOperation? lambda = null;
        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value is IDelegateCreationOperation delegateCreation)
                value = delegateCreation.Target.UnwrapConversions();

            if (value is not IAnonymousFunctionOperation anonymousFunction) continue;
            if (lambda != null) return false;
            lambda = anonymousFunction;
        }

        if (lambda == null || lambda.Symbol.Parameters.Length != 1) return false;

        var body = lambda.Body;
        if (body.Operations.Length != 1 ||
            body.Operations[0] is not IReturnOperation { ReturnedValue: { } returned })
        {
            return false;
        }

        predicate = returned;
        parameter = lambda.Symbol.Parameters[0];
        return true;
    }

    private static void SplitConjuncts(IOperation operation, List<IOperation> conjuncts)
    {
        var unwrapped = UnwrapParentheses(operation);
        if (unwrapped is IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd, OperatorMethod: null } and)
        {
            SplitConjuncts(and.LeftOperand, conjuncts);
            SplitConjuncts(and.RightOperand, conjuncts);
            return;
        }

        conjuncts.Add(unwrapped);
    }

    private static IOperation UnwrapParentheses(IOperation operation)
    {
        var current = operation;
        while (current is IParenthesizedOperation parenthesized)
            current = parenthesized.Operand;

        return current;
    }

    // Structural equality modulo the lambda parameter: same members, same captured values, same operators.
    // Anything outside the small supported shape set compares unequal, which keeps the rule reporting.
    private static bool AreEquivalent(IOperation left, IParameterSymbol leftParameter, IOperation right, IParameterSymbol rightParameter)
    {
        left = UnwrapParentheses(left);
        right = UnwrapParentheses(right);

        switch (left)
        {
            case IBinaryOperation leftBinary when right is IBinaryOperation rightBinary:
                if (leftBinary.OperatorKind != rightBinary.OperatorKind ||
                    leftBinary.IsLifted != rightBinary.IsLifted ||
                    leftBinary.IsChecked != rightBinary.IsChecked ||
                    !SymbolEqualityComparer.Default.Equals(leftBinary.OperatorMethod, rightBinary.OperatorMethod))
                {
                    return false;
                }

                if (AreEquivalent(leftBinary.LeftOperand, leftParameter, rightBinary.LeftOperand, rightParameter) &&
                    AreEquivalent(leftBinary.RightOperand, leftParameter, rightBinary.RightOperand, rightParameter))
                {
                    return true;
                }

                return leftBinary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
                       AreEquivalent(leftBinary.LeftOperand, leftParameter, rightBinary.RightOperand, rightParameter) &&
                       AreEquivalent(leftBinary.RightOperand, leftParameter, rightBinary.LeftOperand, rightParameter);

            case IUnaryOperation leftUnary when right is IUnaryOperation rightUnary:
                return leftUnary.OperatorKind == rightUnary.OperatorKind &&
                       leftUnary.IsLifted == rightUnary.IsLifted &&
                       SymbolEqualityComparer.Default.Equals(leftUnary.OperatorMethod, rightUnary.OperatorMethod) &&
                       AreEquivalent(leftUnary.Operand, leftParameter, rightUnary.Operand, rightParameter);

            case IConversionOperation leftConversion when right is IConversionOperation rightConversion:
                return SymbolEqualityComparer.Default.Equals(leftConversion.Type, rightConversion.Type) &&
                       SymbolEqualityComparer.Default.Equals(leftConversion.OperatorMethod, rightConversion.OperatorMethod) &&
                       AreEquivalent(leftConversion.Operand, leftParameter, rightConversion.Operand, rightParameter);

            case IParameterReferenceOperation leftReference when right is IParameterReferenceOperation rightReference:
                var leftIsLambda = SymbolEqualityComparer.Default.Equals(leftReference.Parameter, leftParameter);
                var rightIsLambda = SymbolEqualityComparer.Default.Equals(rightReference.Parameter, rightParameter);
                if (leftIsLambda || rightIsLambda) return leftIsLambda && rightIsLambda;

                return SymbolEqualityComparer.Default.Equals(leftReference.Parameter, rightReference.Parameter);

            case ILocalReferenceOperation leftLocal when right is ILocalReferenceOperation rightLocal:
                return SymbolEqualityComparer.Default.Equals(leftLocal.Local, rightLocal.Local);

            case IInstanceReferenceOperation leftInstance when right is IInstanceReferenceOperation rightInstance:
                return leftInstance.ReferenceKind == rightInstance.ReferenceKind;

            case IFieldReferenceOperation leftField when right is IFieldReferenceOperation rightField:
                return SymbolEqualityComparer.Default.Equals(leftField.Field, rightField.Field) &&
                       AreEquivalentInstances(leftField.Instance, leftParameter, rightField.Instance, rightParameter);

            case IPropertyReferenceOperation leftProperty when right is IPropertyReferenceOperation rightProperty:
                return leftProperty.Arguments.Length == 0 &&
                       rightProperty.Arguments.Length == 0 &&
                       SymbolEqualityComparer.Default.Equals(leftProperty.Property, rightProperty.Property) &&
                       AreEquivalentInstances(leftProperty.Instance, leftParameter, rightProperty.Instance, rightParameter);

            case ILiteralOperation leftLiteral when right is ILiteralOperation rightLiteral:
                return leftLiteral.ConstantValue.HasValue &&
                       rightLiteral.ConstantValue.HasValue &&
                       SymbolEqualityComparer.Default.Equals(leftLiteral.Type, rightLiteral.Type) &&
                       Equals(leftLiteral.ConstantValue.Value, rightLiteral.ConstantValue.Value);

            default:
                return false;
        }
    }

    private static bool AreEquivalentInstances(IOperation? left, IParameterSymbol leftParameter, IOperation? right, IParameterSymbol rightParameter)
    {
        if (left == null || right == null) return left == null && right == null;

        return AreEquivalent(left, leftParameter, right, rightParameter);
    }
}
