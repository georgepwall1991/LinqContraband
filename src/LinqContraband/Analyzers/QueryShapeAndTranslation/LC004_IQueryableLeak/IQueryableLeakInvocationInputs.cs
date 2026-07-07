using System.Collections.Generic;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private IEnumerable<InvocationInput> EnumerateInvocationInputs(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var originalTargetMethod = GetOriginalTargetMethod(targetMethod);

        if (targetMethod.ReducedFrom != null && invocation.Instance != null && originalTargetMethod.Parameters.Length > 0)
        {
            yield return new InvocationInput(invocation.Instance, originalTargetMethod.Parameters[0]);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null)
                continue;

            var parameter = argument.Parameter;
            if (targetMethod.ReducedFrom != null)
            {
                var originalOrdinal = parameter.Ordinal + 1;
                if (originalOrdinal >= originalTargetMethod.Parameters.Length)
                    continue;

                parameter = originalTargetMethod.Parameters[originalOrdinal];
            }

            yield return new InvocationInput(argument.Value, parameter);
        }
    }
}
