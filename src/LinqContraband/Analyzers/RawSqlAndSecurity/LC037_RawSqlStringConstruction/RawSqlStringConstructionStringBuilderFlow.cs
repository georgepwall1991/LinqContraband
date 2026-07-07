using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool IsStringBuilderAppendArgumentNonConstant(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        if (current is ILocalReferenceOperation localReference)
        {
            if (depth >= MaxLocalResolutionDepth)
                return true;

            if (HasLatestNonConstantLocalWriteBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
                return true;

            if (executableRoot != null &&
                HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
            {
                return false;
            }

            if (HasNonConstantLoopCarriedLocalWrite(localReference.Local, localReference, executableRoot, depth + 1))
                return true;

            if (HasOnlyConstantLocalWritesBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
                return false;

            if (executableRoot != null &&
                TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out var guaranteedValue, out _, out _))
            {
                return IsStringBuilderAppendArgumentNonConstant(guaranteedValue, executableRoot, depth + 1);
            }

            if (TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue))
                return IsStringBuilderAppendArgumentNonConstant(resolvedValue, executableRoot, depth + 1);

            return true;
        }

        if (current is IInvocationOperation invocation)
        {
            if (IsStringConcat(invocation.TargetMethod) ||
                IsStringFormat(invocation.TargetMethod) ||
                IsStringBuilderToString(invocation))
            {
                return IsSuspiciousInvocation(invocation, executableRoot, depth + 1);
            }

            return true;
        }

        return IsNonConstant(current, executableRoot, depth + 1);
    }

}
