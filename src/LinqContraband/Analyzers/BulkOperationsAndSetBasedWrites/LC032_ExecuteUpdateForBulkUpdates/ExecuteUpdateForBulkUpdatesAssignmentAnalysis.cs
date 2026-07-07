using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static bool HasOnlyDirectScalarAssignments(IOperation body, ILocalSymbol iterationLocal)
    {
        var statements = body is IBlockOperation block
            ? block.Operations
            : ImmutableArray.Create(body);

        if (statements.Length == 0)
            return false;

        foreach (var statement in statements)
        {
            if (statement is not IExpressionStatementOperation expressionStatement)
                return false;

            if (expressionStatement.Operation.UnwrapConversions() is not ISimpleAssignmentOperation assignment)
                return false;

            if (!IsDirectScalarAssignment(assignment, iterationLocal))
                return false;
        }

        return true;
    }

    private static bool IsDirectScalarAssignment(ISimpleAssignmentOperation assignment, ILocalSymbol iterationLocal)
    {
        var target = assignment.Target.UnwrapConversions();
        if (target is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not ILocalReferenceOperation localReference ||
            !SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal))
        {
            return false;
        }

        if (!IsScalarLikeType(propertyReference.Property.Type))
            return false;

        return IsSafeScalarValueExpression(assignment.Value, iterationLocal);
    }

    private static bool IsSafeScalarValueExpression(IOperation operation, ILocalSymbol iterationLocal)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return true;

        return current switch
        {
            IDefaultValueOperation => true,
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IPropertyReferenceOperation propertyReference => IsSafeMemberReference(propertyReference.Instance, propertyReference.Type, iterationLocal),
            IFieldReferenceOperation fieldReference => IsSafeFieldReference(fieldReference, iterationLocal),
            IBinaryOperation binaryOperation =>
                IsSafeScalarValueExpression(binaryOperation.LeftOperand, iterationLocal) &&
                IsSafeScalarValueExpression(binaryOperation.RightOperand, iterationLocal),
            IUnaryOperation unaryOperation => IsSafeScalarValueExpression(unaryOperation.Operand, iterationLocal),
            _ => false
        };
    }

    private static bool IsSafeMemberReference(IOperation? instance, ITypeSymbol? memberType, ILocalSymbol iterationLocal)
    {
        if (!IsScalarLikeType(memberType))
            return false;

        if (instance == null)
            return false;

        var unwrappedInstance = instance.UnwrapConversions();
        return unwrappedInstance is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal);
    }

    private static bool IsSafeFieldReference(IFieldReferenceOperation fieldReference, ILocalSymbol iterationLocal)
    {
        if (!IsScalarLikeType(fieldReference.Type))
            return false;

        if (fieldReference.Instance == null)
            return fieldReference.Field.ContainingType.TypeKind == TypeKind.Enum;

        return fieldReference.Instance.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal);
    }

}
