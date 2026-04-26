using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool IsOwnedBySpecificRawSqlAnalyzer(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation)
            return true;

        return current is IBinaryOperation binary &&
               binary.OperatorKind == BinaryOperatorKind.Add &&
               IsConcatWithNonConstant(binary, executableRoot);
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot),
            _ => false
        };
    }

    private static bool IsConcatWithNonConstant(IBinaryOperation binary, IOperation? executableRoot)
    {
        return IsNonConstant(binary.LeftOperand, executableRoot) || IsNonConstant(binary.RightOperand, executableRoot);
    }

    private static bool IsNonConstant(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot),
            IFieldReferenceOperation => true,
            IPropertyReferenceOperation => true,
            _ => true
        };
    }

    private static bool IsSuspiciousInvocation(IInvocationOperation invocation, IOperation? executableRoot)
    {
        var method = invocation.TargetMethod;

        if (IsStringFormat(method))
            return invocation.Arguments.Any(arg => !arg.Value.UnwrapConversions().ConstantValue.HasValue);

        if (IsStringConcat(method))
            return invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot));

        if (IsStringBuilderToString(invocation))
            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);

        return false;
    }

    private static bool IsStringFormat(IMethodSymbol method)
    {
        return method.Name == "Format" &&
               method.ContainingType.Name == "String" &&
               method.ContainingNamespace?.ToString() == "System";
    }

    private static bool IsStringConcat(IMethodSymbol method)
    {
        return method.Name == "Concat" &&
               method.ContainingType.Name == "String" &&
               method.ContainingNamespace?.ToString() == "System";
    }

    private static bool IsStringBuilderToString(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "ToString" &&
               invocation.GetInvocationReceiver()?.Type is INamedTypeSymbol receiverType &&
               receiverType.Name == "StringBuilder" &&
               receiverType.ContainingNamespace?.ToString() == "System.Text";
    }

    private static bool ContainsSuspiciousStringBuilderAppend(IOperation? receiver, IOperation? executableRoot)
    {
        if (receiver == null)
            return false;

        var current = receiver.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (IsStringBuilderAppend(invocation.TargetMethod))
            {
                if (invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot)))
                    return true;

                return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);
            }

            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);
        }

        if (current is ILocalReferenceOperation localReference)
        {
            return TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                   ContainsSuspiciousStringBuilderAppend(resolvedValue, executableRoot);
        }

        return false;
    }

    private static bool IsStringBuilderAppend(IMethodSymbol method)
    {
        return method.ContainingType.Name == nameof(StringBuilder) &&
               method.ContainingNamespace?.ToString() == "System.Text" &&
               method.Name.StartsWith("Append", System.StringComparison.Ordinal);
    }
}
