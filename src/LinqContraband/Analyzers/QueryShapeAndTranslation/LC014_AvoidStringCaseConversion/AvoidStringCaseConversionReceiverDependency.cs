using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC014_AvoidStringCaseConversion;

public sealed partial class AvoidStringCaseConversionAnalyzer
{
    private bool ReceiverDependsOnParameter(IOperation? operation, ImmutableArray<IParameterSymbol> targetParameters)
    {
        if (operation == null) return false;

        // Unwrap conversions
        operation = operation!.UnwrapConversions();

        // If it's a parameter reference, check if it matches our target lambda parameters
        if (operation is IParameterReferenceOperation paramRef) return targetParameters.Contains(paramRef.Parameter);

        // If it's a property reference, check the instance of the property
        if (operation is IPropertyReferenceOperation propRef)
            return ReceiverDependsOnParameter(propRef.Instance, targetParameters);

        // If it's a method call (chained), check the instance AND the arguments. The value
        // depends on the parameter when the call is on a parameter-derived receiver
        // (e.g. u.Name.Substring(..)) OR when a column-derived value is passed as an argument
        // (e.g. string.Concat(u.A, u.B), a static method with no instance) — both produce a
        // value over which a case conversion still defeats sargability.
        if (operation is IInvocationOperation invocation)
        {
            if (ReceiverDependsOnParameter(invocation.Instance, targetParameters))
                return true;

            foreach (var argument in invocation.Arguments)
            {
                // Only an argument that can carry the column's text into the result makes the
                // cased value column-derived. A value-type argument that just controls
                // length/position/format (an `int` count/index, a `bool`, an enum such as
                // `StringComparison`) does not — e.g. "CONST".PadRight(u.Name.Length) or
                // "HELLO".Substring(0, u.Age) lowercases a constant, so it must stay quiet.
                // A `char` argument is the exception: it contributes a character to the result
                // (string.Concat(u.Name[0]), "x".Replace('x', u.Name[0])), so it is still
                // followed. String, string[]/object[] params arrays, and string collections are
                // reference types and are likewise followed (string.Concat(u.A, u.B),
                // "p".Replace("x", u.Name)).
                var argumentValue = argument.Value.UnwrapConversions();
                if (!ArgumentCanCarryStringContent(argumentValue.Type))
                    continue;

                if (ReceiverDependsOnParameter(argumentValue, targetParameters))
                    return true;
            }

            return false;
        }

        // A params argument (e.g. the value[] of string.Join / string.Format) is wrapped in an
        // array creation, so look at the column-derived elements inside it.
        if (operation is IArrayCreationOperation arrayCreation && arrayCreation.Initializer != null)
        {
            foreach (var element in arrayCreation.Initializer.ElementValues)
            {
                if (ReceiverDependsOnParameter(element, targetParameters))
                    return true;
            }

            return false;
        }

        // If it's an array/indexer access
        if (operation is IPropertyReferenceOperation indexer && indexer.Arguments.Length > 0)
            return ReceiverDependsOnParameter(indexer.Instance, targetParameters);

        // Binary Operator
        if (operation is IBinaryOperation binaryOp)
            return ReceiverDependsOnParameter(binaryOp.LeftOperand, targetParameters) ||
                   ReceiverDependsOnParameter(binaryOp.RightOperand, targetParameters);

        // Coalesce Operator
        if (operation is ICoalesceOperation coalesce)
            return ReceiverDependsOnParameter(coalesce.Value, targetParameters) ||
                   ReceiverDependsOnParameter(coalesce.WhenNull, targetParameters);

        if (operation.Kind == OperationKind.ConditionalAccess)
        {
            var conditional = (IConditionalAccessOperation)operation;
            return ReceiverDependsOnParameter(conditional.Operation, targetParameters);
        }

        if (operation.Kind == OperationKind.ConditionalAccessInstance)
        {
            var parent = operation.Parent; // Invocation (ToLower)
            var grandParent = parent?.Parent; // ConditionalAccessOperation

            if (grandParent is IConditionalAccessOperation caOp)
                return ReceiverDependsOnParameter(caOp.Operation, targetParameters);
        }

        return false;
    }
}
