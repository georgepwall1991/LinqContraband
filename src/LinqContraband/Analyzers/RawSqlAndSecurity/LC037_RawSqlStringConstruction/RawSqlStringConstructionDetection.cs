using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private const int MaxLocalResolutionDepth = 32;

    private static bool IsOwnedBySpecificRawSqlAnalyzer(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation)
            return true;

        return current is IBinaryOperation binary &&
               binary.OperatorKind == BinaryOperatorKind.Add &&
               IsConcatWithNonConstant(binary, executableRoot, depth: 0);
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot)
    {
        return IsConstructedRawSql(operation, executableRoot, depth: 0);
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot, depth + 1),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot, depth + 1),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        depth < MaxLocalResolutionDepth &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot, depth + 1),
            _ => false
        };
    }

    private static bool IsConcatWithNonConstant(IBinaryOperation binary, IOperation? executableRoot, int depth)
    {
        return IsNonConstant(binary.LeftOperand, executableRoot, depth + 1) ||
               IsNonConstant(binary.RightOperand, executableRoot, depth + 1);
    }

    private static bool IsNonConstant(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot, depth + 1),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot, depth + 1),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        depth < MaxLocalResolutionDepth &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot, depth + 1),
            IFieldReferenceOperation => true,
            IPropertyReferenceOperation => true,
            _ => true
        };
    }

    private static bool IsSuspiciousInvocation(IInvocationOperation invocation, IOperation? executableRoot, int depth)
    {
        var method = invocation.TargetMethod;

        if (IsStringFormat(method))
            return invocation.Arguments.Any(arg => !arg.Value.UnwrapConversions().ConstantValue.HasValue);

        if (IsStringConcat(method))
            return invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot, depth + 1));

        if (IsStringBuilderToString(invocation))
            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);

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

}
